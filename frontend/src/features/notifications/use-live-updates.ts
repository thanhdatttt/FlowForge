import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { accessToken } from '@/lib/api'
import { useSessionStore } from '@/features/auth/session-store'
import { notificationKey } from './api'
import { createLiveUpdates, type LiveScope, type LiveState } from './realtime'

export function useLiveUpdates({ workspaceId, projectId }: LiveScope) {
  const client = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')
  const live = useRef<ReturnType<typeof createLiveUpdates> | null>(null)
  const scopeRef = useRef<LiveScope>({ workspaceId, projectId })
  const retryRef = useRef<() => void>(() => {})

  useEffect(() => {
    let active = true
    let initializing = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let notifications = false
    let controller: ReturnType<typeof createLiveUpdates> | undefined
    const projects = new Map<string, LiveScope>()
    const flush = () => {
      timer = undefined
      if (!active || !useSessionStore.getState().tokens) return
      if (notifications)
        void client.invalidateQueries({ queryKey: notificationKey })
      notifications = false
      for (const scope of projects.values()) {
        void client.invalidateQueries({
          queryKey: ['phase3', scope.workspaceId, scope.projectId],
        })
        void client.invalidateQueries({
          queryKey: ['phase2', scope.workspaceId],
        })
      }
      projects.clear()
    }
    const queue = () => { timer ??= setTimeout(flush, 150) }

    async function initialize() {
      if (controller) { controller.retry(); return }
      if (initializing) return
      initializing = true
      try {
        // Load the transport only for signed-in users.
        const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr')
        if (!active) return
        const connection = new HubConnectionBuilder()
          .withUrl('/hubs/updates', { accessTokenFactory: accessToken })
          .configureLogging(LogLevel.None)
          .build()
        controller = createLiveUpdates(connection, {
          state: value => { if (active) setState(value) },
          recover: async () => {
            if (!active || !useSessionStore.getState().tokens) return
            // Active queries refetch missed events and current permissions.
            // Inactive queries become stale and refetch when opened.
            await Promise.all([
              client.invalidateQueries({ queryKey: ['phase2'] }),
              client.invalidateQueries({ queryKey: ['phase3'] }),
              client.invalidateQueries({ queryKey: notificationKey }),
            ])
          },
          notification: () => { notifications = true; queue() },
          project: event => {
            projects.set(`${event.workspaceId}/${event.projectId}`, {
              workspaceId: event.workspaceId,
              projectId: event.projectId || undefined,
            })
            queue()
          },
        })
        live.current = controller
        if (scopeRef.current.workspaceId)
          controller.setScope(scopeRef.current)
        else controller.start()
      } catch {
        if (active) setState('offline')
      } finally {
        initializing = false
      }
    }
    retryRef.current = () => { void initialize() }
    void initialize()
    const resume = () => retryRef.current()
    const visible = () => {
      if (document.visibilityState === 'visible') resume()
    }
    window.addEventListener('online', resume)
    document.addEventListener('visibilitychange', visible)
    return () => {
      active = false
      if (timer !== undefined) clearTimeout(timer)
      window.removeEventListener('online', resume)
      document.removeEventListener('visibilitychange', visible)
      live.current = null
      void controller?.stop().catch(() => {})
    }
  }, [client])

  useEffect(() => {
    scopeRef.current = { workspaceId, projectId }
    live.current?.setScope(scopeRef.current)
  }, [workspaceId, projectId])
  return { state, retry: () => retryRef.current() }
}

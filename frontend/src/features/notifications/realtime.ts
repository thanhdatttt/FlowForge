import type { ProjectUpdate } from './api'

export type LiveState = 'connecting' | 'connected' | 'reconnecting' | 'offline'
export type LiveScope = { workspaceId?: string; projectId?: string }
// Structural interface allows lifecycle/race tests without a mock web server.
export interface LiveConnection {
  start(): Promise<void>
  stop(): Promise<void>
  invoke(method: string, ...args: string[]): Promise<unknown>
  on(name: string, handler: (...args: any[]) => void): void
  off(name: string, handler: (...args: any[]) => void): void
  onclose(handler: () => void): void
}
export function eventWindow(limit = 1000) {
  const ids = new Set<string>()
  return (id: string) => {
    if (!id || ids.has(id)) return false
    ids.add(id)
    if (ids.size > limit) ids.delete(ids.values().next().value!)
    return true
  }
}

// A single serialized loop owns start/join/leave/recovery. SignalR's internal
// reconnect is deliberately not enabled: this also retries initial start and
// joins, and always completes subscriptions before authoritative REST recovery.
export function createLiveUpdates(connection: LiveConnection, options: {
  state: (state: LiveState) => void
  recover: () => Promise<unknown>
  notification: () => void
  project: (event: ProjectUpdate) => void
  schedule?: (run: () => void, delay: number) => ReturnType<typeof setTimeout>
  cancel?: (timer: ReturnType<typeof setTimeout>) => void
}) {
  let disposed = false
  let connected = false
  let desired: LiveScope = {}
  let joined: LiveScope = {}
  let revision = 0
  let running = false
  let again = false
  let recovering = false
  let attempt = 0
  let timer: ReturnType<typeof setTimeout> | undefined
  const schedule = options.schedule || setTimeout
  const cancel = options.cancel || clearTimeout
  const eventSeen = eventWindow()
  const notificationSeen = eventWindow()
  const notification = (event: { id: string }) => {
    if (!disposed && connected && typeof event?.id === 'string' && notificationSeen(event.id)) options.notification()
  }
  const project = (event: ProjectUpdate) => {
    if (disposed || !connected || !event || typeof event.eventId !== 'string' || !event.workspaceId) return
    if (event.workspaceId !== desired.workspaceId || (desired.projectId && event.projectId !== desired.projectId)) return
    if (eventSeen(event.eventId)) options.project(event)
  }
  connection.on('NotificationReceived', notification)
  connection.on('ProjectUpdated', project)
  connection.onclose(() => {
    connected = false
    joined = {}
    revision++
    if (!disposed) {
      options.state('reconnecting')
      retryLater()
    }
  })
  function retryLater() {
    if (disposed || timer !== undefined) return
    timer = schedule(() => { timer = undefined; void sync() }, Math.min(1000 * 2 ** Math.min(attempt++, 5), 30000))
  }
  async function sync() {
    if (disposed) return
    if (running) { again = true; return }
    running = true
    again = false
    const version = revision
    const target = { ...desired }
    try {
      if (!connected) {
        options.state(attempt ? 'reconnecting' : 'connecting')
        await connection.start()
        if (disposed) { await connection.stop(); return }
        connected = true
        joined = {}
      }
      if (joined.projectId && (joined.projectId !== target.projectId || joined.workspaceId !== target.workspaceId)) {
        await connection.invoke('LeaveProject', joined.projectId)
        joined = {}
      }
      if (joined.workspaceId && !joined.projectId && (joined.workspaceId !== target.workspaceId || !!target.projectId)) {
        await connection.invoke('LeaveWorkspace', joined.workspaceId)
        joined.workspaceId = undefined
      }
      // Subscribe to the active project, or to the workspace overview. Avoid
      // overlapping groups; duplicate delivery is still tolerated by event IDs.
      if (target.workspaceId && target.projectId && (joined.projectId !== target.projectId || joined.workspaceId !== target.workspaceId)) {
        await connection.invoke('JoinProject', target.workspaceId, target.projectId)
        joined = { ...target }
      } else if (target.workspaceId && !target.projectId && joined.workspaceId !== target.workspaceId) {
        await connection.invoke('JoinWorkspace', target.workspaceId)
        joined = { workspaceId: target.workspaceId }
      }
      if (disposed || version !== revision || !connected) return
      recovering = true
      await options.recover()
      if (disposed || version !== revision || !connected) return
      attempt = 0
      options.state('connected')
    } catch {
      if (!disposed) {
        options.state('offline')
        // Failed subscription may mean lost permission; REST must check current
        // access even though no more group events will reach this connection.
        if (!recovering) await options.recover().catch(() => {})
        retryLater()
      }
    } finally {
      recovering = false
      running = false
      if (!disposed && (again || version !== revision)) void sync()
    }
  }
  return {
    setScope(scope: LiveScope) {
      if (scope.workspaceId === desired.workspaceId && scope.projectId === desired.projectId) return
      desired = { ...scope }
      revision++
      void sync()
    },
    start: () => { void sync() },
    retry() {
      if (timer !== undefined) { cancel(timer); timer = undefined }
      attempt = 0
      void sync()
    },
    async stop() {
      disposed = true
      if (timer !== undefined) cancel(timer)
      connection.off('NotificationReceived', notification)
      connection.off('ProjectUpdated', project)
      await connection.stop()
    },
  }
}

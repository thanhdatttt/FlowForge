import { useEffect, useRef, useState } from 'react'
import { ApiError } from '@/lib/api'

// Local, cancellable mutations: prompts and preview tokens never enter Query cache/storage.
export function useAiRequest() {
  const current = useRef<AbortController | null>(null)
  const mounted = useRef(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [retryAt, setRetryAt] = useState(0)
  const [now, setNow] = useState(Date.now)
  const cooldown = Math.max(0, Math.ceil((retryAt - now) / 1000))
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      current.current?.abort()
    }
  }, [])
  useEffect(() => {
    if (!cooldown) return
    const timer = setTimeout(() => setNow(Date.now()), 1000)
    return () => clearTimeout(timer)
  }, [retryAt, cooldown])
  const cancel = () => {
    current.current?.abort()
    current.current = null
    setBusy(false)
  }
  async function run<T>(
    request: (signal: AbortSignal) => Promise<T>,
    success: (value: T) => void,
    failed?: (error: unknown) => void,
  ) {
    if (current.current || Date.now() < retryAt) return
    const controller = new AbortController()
    current.current = controller
    setBusy(true)
    setError(null)
    try {
      const result = await request(controller.signal)
      if (mounted.current && current.current === controller) success(result)
    } catch (failure) {
      if (mounted.current && current.current === controller) {
        setError(failure)
        failed?.(failure)
        if (failure instanceof ApiError && failure.status === 429) {
          setRetryAt(Date.now() + (failure.retryAfterSeconds || 60) * 1000)
          setNow(Date.now())
        }
      }
    } finally {
      if (mounted.current && current.current === controller) {
        current.current = null
        setBusy(false)
      }
    }
  }
  return {
    busy,
    error,
    setError,
    run,
    cancel,
    cooldown,
  }
}

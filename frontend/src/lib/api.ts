import { useSessionStore, type AuthTokens } from '@/features/auth/session-store'

export const API_ORIGIN = (
  import.meta.env?.VITE_API_BASE_URL ?? ''
)
  .trim()
  .replace(/\/+$/, '')

export class ApiError extends Error {
  status: number
  fields: Record<string, string[]>
  retryAfterSeconds?: number
  constructor(
    message: string,
    status: number,
    fields: Record<string, string[]> = {},
    retryAfterSeconds?: number,
  ) {
    super(message)
    this.status = status
    this.fields = fields
    this.retryAfterSeconds = retryAfterSeconds
  }
}

async function request<T>(
  path: string,
  init: RequestInit = {},
  responseType: 'json' | 'blob' = 'json',
): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${API_ORIGIN}/api/v1${path}`, {
      ...init,
      headers: {
        ...(init.body && !(init.body instanceof FormData)
          ? { 'Content-Type': 'application/json' }
          : {}),
        ...init.headers,
      },
      signal: init.signal ?? AbortSignal.timeout(30_000),
    })
  } catch {
    throw new ApiError(
      'Couldn’t connect to FlowForge. Check your connection and try again.',
      0,
    )
  }
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as {
      title?: string
      errors?: Record<string, string[]>
    } | null
    const fallback =
      response.status === 429
        ? 'Too many attempts. Please wait a minute and try again.'
        : response.status >= 500
          ? 'FlowForge is unavailable right now. Please try again shortly.'
          : 'The request couldn’t be completed. Please try again.'
    throw new ApiError(
      response.status >= 500 ? fallback : problem?.title || fallback,
      response.status,
      problem?.errors,
      response.status === 429
        ? retryAfter(response.headers.get('Retry-After'))
        : undefined,
    )
  }
  return response.status === 204
    ? (undefined as T)
    : ((responseType === 'blob'
        ? response.blob()
        : response.json()) as Promise<T>)
}

// Optional response metadata; existing request, refresh and error behavior is unchanged.
function retryAfter(value: string | null): number | undefined {
  if (!value) return undefined
  const seconds = /^\d+$/.test(value)
    ? Number(value)
    : Math.ceil((Date.parse(value) - Date.now()) / 1000)
  return Number.isFinite(seconds)
    ? Math.min(3600, Math.max(1, seconds))
    : undefined
}

let refreshPromise: Promise<AuthTokens> | null = null
// Serialize cookie rotation across tabs as well as React StrictMode/HTTP/SignalR.
export function sessionApi<T>(path: string, init: RequestInit = {}): Promise<T> {
  const send = () =>
    request<T>(path, {
      ...init,
      headers: { ...init.headers, 'X-FlowForge-Session': 'browser' },
    })
  return globalThis.navigator?.locks
    ? navigator.locks.request('flowforge-session', send)
    : send()
}

let restorePromise: Promise<void> | null = null
export function restoreSession(): Promise<void> {
  if (!restorePromise) {
    const initial = useSessionStore.getState().tokens
    restorePromise = sessionApi<AuthTokens>('/auth/session', { method: 'POST' })
      .then((tokens) => {
        if (useSessionStore.getState().tokens === initial)
          useSessionStore.getState().setTokens(tokens)
      })
      .catch((error: unknown) => {
        if (!(error instanceof ApiError) || error.status !== 401) throw error
      })
      .finally(() => {
        restorePromise = null
      })
  }
  return restorePromise
}

async function refresh() {
  const tokens = useSessionStore.getState().tokens
  if (!tokens)
    throw new ApiError('Your session has ended. Please sign in again.', 401)
  if (!refreshPromise) {
    refreshPromise = sessionApi<AuthTokens>('/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken: tokens.refreshToken }),
    })
      .then((next) => {
        // A late refresh must never restore a session that has signed out.
        if (
          useSessionStore.getState().tokens?.refreshToken !==
          tokens.refreshToken
        )
          throw new ApiError(
            'Your session has ended. Please sign in again.',
            401,
          )
        useSessionStore.getState().setTokens(next)
        return next
      })
      .catch((error: unknown) => {
        if (
          error instanceof ApiError &&
          error.status === 401 &&
          useSessionStore.getState().tokens?.refreshToken ===
            tokens.refreshToken
        )
          useSessionStore.getState().clear()
        throw error
      })
      .finally(() => {
        refreshPromise = null
      })
  }
  return refreshPromise
}

// Share rotation with HTTP requests: SignalR reconnects must not reuse an expired
// access token or independently rotate the same single-use refresh token.
export async function accessToken() {
  const tokens = useSessionStore.getState().tokens
  if (!tokens)
    throw new ApiError('Your session has ended. Please sign in again.', 401)
  return (Date.parse(tokens.accessTokenExpiresAt) <= Date.now() + 30_000
    ? await refresh()
    : tokens).accessToken
}

export async function api<T>(
  path: string,
  init: RequestInit = {},
  authenticated = false,
  responseType: 'json' | 'blob' = 'json',
): Promise<T> {
  if (!authenticated) return request<T>(path, init, responseType)
  let tokens = useSessionStore.getState().tokens
  if (!tokens)
    throw new ApiError('Your session has ended. Please sign in again.', 401)
  if (Date.parse(tokens.accessTokenExpiresAt) <= Date.now() + 30_000)
    tokens = await refresh()
  const send = (accessToken: string) =>
    request<T>(
      path,
      {
        ...init,
        headers: { ...init.headers, Authorization: `Bearer ${accessToken}` },
      },
      responseType,
    )
  try {
    return await send(tokens.accessToken)
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401) throw error
    const current = useSessionStore.getState().tokens
    const next =
      current && current.accessToken !== tokens.accessToken
        ? current
        : await refresh()
    try {
      return await send(next.accessToken)
    } catch (retryError) {
      if (retryError instanceof ApiError && retryError.status === 401)
        useSessionStore.getState().clear()
      throw retryError
    }
  }
}

import { api } from '@/lib/api'
import type { AuthTokens } from './session-store'

export type Credentials = {
  email: string
  password: string
  displayName?: string
}
export type CurrentUser = {
  id: string
  email: string
  displayName: string
  emailVerified: boolean
}

export const authApi = {
  signIn: (data: Credentials) =>
    api<AuthTokens>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email: data.email, password: data.password }),
    }),
  register: (data: Credentials) =>
    api<AuthTokens>('/auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  me: (signal?: AbortSignal) => api<CurrentUser>('/auth/me', { signal }, true),
  signOut: (refreshToken: string) =>
    api<void>('/auth/logout', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    }),
  requestCode: () =>
    api<void>('/auth/email-verification/request', { method: 'POST' }, true),
  confirmCode: (code: string) =>
    api<void>(
      '/auth/email-verification/confirm',
      { method: 'POST', body: JSON.stringify({ code }) },
      true,
    ),
}

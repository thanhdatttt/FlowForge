import { create } from 'zustand'

export type AuthTokens = {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
}

// Tokens live in memory. Startup restores them using the backend's HttpOnly cookie.
export const useSessionStore = create<{
  tokens: AuthTokens | null
  setTokens: (tokens: AuthTokens) => void
  clear: () => void
}>((set) => ({
  tokens: null,
  setTokens: (tokens) => set({ tokens }),
  clear: () => set({ tokens: null }),
}))

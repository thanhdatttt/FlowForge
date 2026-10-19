import { create } from 'zustand'

export type AuthTokens = {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
}

// Tokens deliberately live only in memory. Reloading requires a new sign-in;
// persistent browser storage is not a secure substitute for an HttpOnly cookie.
export const useSessionStore = create<{
  tokens: AuthTokens | null
  setTokens: (tokens: AuthTokens) => void
  clear: () => void
}>((set) => ({
  tokens: null,
  setTokens: (tokens) => set({ tokens }),
  clear: () => set({ tokens: null }),
}))

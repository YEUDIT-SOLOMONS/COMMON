import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { authApi } from '../api/endpoints.js'
import { getStoredToken, onSessionExpired, setStoredToken } from '../api/client.js'

const USER_STORAGE_KEY = 'photorental.user'
const AuthContext = createContext(null)

export const SESSION_EXPIRED_KEY = 'photorental.sessionExpired'

function isTokenValid(token) {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    return !payload.exp || payload.exp * 1000 > Date.now()
  } catch {
    return false
  }
}

function readStoredUser() {
  try {
    const raw = localStorage.getItem(USER_STORAGE_KEY)
    if (!raw) return null
    const token = getStoredToken()
    if (!token || !isTokenValid(token)) {
      localStorage.removeItem(USER_STORAGE_KEY)
      setStoredToken(null)
      sessionStorage.setItem(SESSION_EXPIRED_KEY, '1')
      return null
    }
    return JSON.parse(raw)
  } catch {
    return null
  }
}

export function AuthProvider({ children }) {
  const [user, setUser] = useState(readStoredUser)

  const applyAuthResponse = useCallback((authResponse) => {
    setStoredToken(authResponse.token)
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(authResponse.user))
    setUser(authResponse.user)
    return authResponse.user
  }, [])

  const login = useCallback(
    async (email, password) => {
      const response = await authApi.login({ email, password })
      return applyAuthResponse(response)
    },
    [applyAuthResponse],
  )

  const register = useCallback(
    async ({ name, email, phone, password, termsAccepted, ...quiet }) => {
      const response = await authApi.register({ name, email, phone, password, termsAccepted, ...quiet })
      return applyAuthResponse(response)
    },
    [applyAuthResponse],
  )

  const loginWithGoogle = useCallback(
    async (idToken) => {
      const response = await authApi.google(idToken)
      return applyAuthResponse(response)
    },
    [applyAuthResponse],
  )

  const logout = useCallback(() => {
    setStoredToken(null)
    localStorage.removeItem(USER_STORAGE_KEY)
    setUser(null)
  }, [])

  useEffect(() => {
    onSessionExpired(() => {
      sessionStorage.setItem(SESSION_EXPIRED_KEY, '1')
      logout()
    })
    return () => onSessionExpired(null)
  }, [logout])

  // מעדכן שדות בודדים במשתמש המחובר (למשל כתובת/עיר לאחר שמירתן) בלי לדרוש התחברות מחדש
  const updateUser = useCallback((partialUser) => {
    setUser((prev) => {
      if (!prev) return prev
      const merged = { ...prev, ...partialUser }
      localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(merged))
      return merged
    })
  }, [])

  const value = useMemo(
    () => ({ user, isAuthenticated: Boolean(user), login, loginWithGoogle, register, logout, updateUser }),
    [user, login, loginWithGoogle, register, logout, updateUser],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth חייב לפעול בתוך AuthProvider')
  }
  return ctx
}

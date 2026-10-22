import { useCallback, useEffect, useState } from 'react';
import {
  clearStoredSession,
  getStoredSession,
  onUnauthorized,
  setStoredSession,
} from '../../api/client';
import type { AuthenticatedUser, LoginResponse } from '../../types/api';
import { AuthContext } from './authContext';

function readActiveUser(): AuthenticatedUser | null {
  const session = getStoredSession();
  if (!session) {
    return null;
  }

  if (session.expiresAtUtc && new Date(session.expiresAtUtc).getTime() <= Date.now()) {
    clearStoredSession();
    return null;
  }

  return session.user;
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthenticatedUser | null>(() => readActiveUser());

  const signIn = useCallback((session: LoginResponse) => {
    setStoredSession(session);
    setUser(session.user);
  }, []);

  const signOut = useCallback(() => {
    clearStoredSession();
    setUser(null);
  }, []);

  useEffect(() => {
    return onUnauthorized(() => {
      setUser(null);
    });
  }, []);

  return (
    <AuthContext.Provider value={{ user, signIn, signOut }}>
      {children}
    </AuthContext.Provider>
  );
}


import { createContext, useContext } from 'react';
import type { AuthenticatedUser, LoginResponse } from '../../types/api';

export interface AuthContextValue {
  user: AuthenticatedUser | null;
  signIn: (session: LoginResponse) => void;
  signOut: () => void;
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider.');
  }

  return context;
}

export function useOptionalAuth(): AuthContextValue | undefined {
  return useContext(AuthContext);
}


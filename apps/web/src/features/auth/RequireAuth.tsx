import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './authContext';

/**
 * Protects routes that require an authenticated agent.
 * Redirects unauthenticated visitors to /login and preserves the path they wanted.
 */
export function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();

  if (!user) {
    return (
      <Navigate
        to="/login"
        state={{ from: `${location.pathname}${location.search}` }}
        replace
      />
    );
  }

  return <>{children}</>;
}


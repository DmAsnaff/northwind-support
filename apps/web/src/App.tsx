import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useOptionalAuth } from './features/auth/authContext';

/** Application shell: the header, the navigation, and wherever the router puts the page. */
export function App() {
  const auth = useOptionalAuth();
  const navigate = useNavigate();

  const handleSignOut = () => {
    auth?.signOut();
    void navigate('/login', { replace: true });
  };

  return (
    <div className="app">
      <header className="app__header">
        <Link className="app__brand" to="/tickets">
          Northwind Support
        </Link>

        <nav className="app__nav" aria-label="Main">
          <NavLink to="/tickets">Tickets</NavLink>
          <NavLink to="/customers">Customers</NavLink>
        </nav>

        {auth?.user && (
          <div className="app__user">
            <span className="app__user-name" aria-label="Signed-in agent">
              {auth.user.fullName}
            </span>
            <button type="button" className="button button--ghost" onClick={handleSignOut}>
              Sign out
            </button>
          </div>
        )}
      </header>

      <main className="app__main">
        <Outlet />
      </main>
    </div>
  );
}

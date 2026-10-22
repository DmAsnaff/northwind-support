import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { authApi } from '../../api/auth';
import { toErrorMessage } from '../../api/client';
import { Field } from '../../components/Field';
import { useAuth } from './authContext';

interface LocationState {
  from?: string;
}

/**
 * Sign-in screen for support agents (AU-9, AU-11).
 */
export function LoginPage() {
  const { user, signIn } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state = location.state as LocationState | null;
  const redirectTarget = state?.from && state.from !== '/login' ? state.from : '/tickets';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [emailError, setEmailError] = useState<string>();
  const [passwordError, setPasswordError] = useState<string>();
  const [submitError, setSubmitError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  if (user) {
    return <Navigate to={redirectTarget} replace />;
  }

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    setEmailError(undefined);
    setPasswordError(undefined);
    setSubmitError(undefined);

    const trimmedEmail = email.trim();
    let hasValidationError = false;

    if (!trimmedEmail || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
      setEmailError('Enter a valid work email address.');
      hasValidationError = true;
    }

    if (password.length === 0) {
      setPasswordError('Enter your password.');
      hasValidationError = true;
    }

    if (hasValidationError) {
      return;
    }

    setIsSubmitting(true);

    try {
      const session = await authApi.login({
        email: trimmedEmail,
        password,
      });
      signIn(session);
      void navigate(redirectTarget, { replace: true });
    } catch (caught) {
      setSubmitError(toErrorMessage(caught, 'Invalid email or password.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <main className="app__main login-page">
      <section className="card login-card" aria-labelledby="login-heading">
        <header className="page-header">
          <div>
            <h1 id="login-heading">Sign in to Northwind Support</h1>
            <p className="page-header__subtitle">
              Use your support agent credentials to access the desk.
            </p>
          </div>
        </header>

        <form className="form" onSubmit={submit} noValidate>
          <Field id="login-email" label="Email" error={emailError}>
            {(fieldProps) => (
              <input
                {...fieldProps}
                type="email"
                autoComplete="email"
                value={email}
                disabled={isSubmitting}
                onChange={(event) => setEmail(event.target.value)}
              />
            )}
          </Field>

          <Field id="login-password" label="Password" error={passwordError}>
            {(fieldProps) => (
              <input
                {...fieldProps}
                type="password"
                autoComplete="current-password"
                value={password}
                disabled={isSubmitting}
                onChange={(event) => setPassword(event.target.value)}
              />
            )}
          </Field>

          {submitError && (
            <p className="field__error" role="alert">
              {submitError}
            </p>
          )}

          <div className="button-row">
            <button type="submit" className="button button--primary" disabled={isSubmitting}>
              {isSubmitting ? 'Signing in...' : 'Sign in'}
            </button>
          </div>
        </form>
      </section>
    </main>
  );
}


import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearStoredSession,
  getStoredSession,
  request,
  setStoredSession,
} from '../../api/client';
import { AuthProvider } from './AuthProvider';
import { LoginPage } from './LoginPage';
import { RequireAuth } from './RequireAuth';

describe('Authentication and session handling', () => {
  beforeEach(() => {
    clearStoredSession();
  });

  afterEach(() => {
    clearStoredSession();
    vi.restoreAllMocks();
  });

  it('redirects unauthenticated visitors from a protected route to /login', () => {
    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/tickets']}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route
              path="/tickets"
              element={
                <RequireAuth>
                  <div>Protected Ticket List</div>
                </RequireAuth>
              }
            />
          </Routes>
        </MemoryRouter>
      </AuthProvider>,
    );

    expect(screen.getByRole('heading', { name: /sign in to northwind support/i })).toBeInTheDocument();
    expect(screen.queryByText('Protected Ticket List')).not.toBeInTheDocument();
  });

  it('attaches the Bearer token to API requests and clears the session on 401', async () => {
    setStoredSession({
      token: 'jwt-test-token-123',
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
      user: { id: 1, fullName: 'Alex Turner', email: 'alex.turner@northwind-support.example' },
    });

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ id: 1, fullName: 'Alex Turner', email: 'alex.turner@northwind-support.example' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await request('/auth/me');

    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/auth/me',
      expect.objectContaining({
        headers: expect.objectContaining({
          Authorization: 'Bearer jwt-test-token-123',
        }),
      }),
    );

    // A 401 response clears the stored session automatically
    fetchSpy.mockResolvedValueOnce(
      new Response(JSON.stringify({ title: 'Unauthorized', detail: 'Token expired', status: 401 }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await expect(request('/tickets')).rejects.toThrow(/Token expired/i);
    expect(getStoredSession()).toBeNull();
  });

  it('shows an error alert when login credentials are rejected with 401', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          title: 'Unauthorized',
          detail: 'Invalid email or password.',
          status: 401,
        }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/login']}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>,
    );

    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: 'alex.turner@northwind-support.example' },
    });
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: 'wrong-password' },
    });
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/invalid email or password/i);
    });
  });
});


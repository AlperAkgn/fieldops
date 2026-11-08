import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { Protected } from './auth';
import { canManage } from './api/session';
import type { Access } from './api/contracts';
const state = vi.hoisted(() => ({ access: null as Access | null }));
vi.mock('./api/client', () => ({
  refresh: vi.fn(),
  sessionStore: { subscribe: () => () => undefined, getSnapshot: () => state.access },
}));
describe('protected routing', () => {
  it('does not render protected content without a session', () => {
    state.access = null;
    render(
      <MemoryRouter>
        <Routes>
          <Route
            path="/"
            element={
              <Protected>
                <p>Secret</p>
              </Protected>
            }
          />
          <Route path="/login" element={<p>Sign in</p>} />
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.queryByText('Secret')).toBeNull();
  });
  it('blocks viewers from manager routes', () => {
    state.access = { role: 'Viewer' } as Access;
    render(
      <MemoryRouter>
        <Protected manager>
          <p>Secret</p>
        </Protected>
      </MemoryRouter>,
    );
    expect(screen.getByText('Access restricted')).toBeTruthy();
    expect(screen.queryByText('Secret')).toBeNull();
  });
  it('does not grant owner access to a manager', () => {
    state.access = { role: 'Manager' } as Access;
    render(
      <MemoryRouter>
        <Protected owner>
          <p>Owner controls</p>
        </Protected>
      </MemoryRouter>,
    );
    expect(screen.queryByText('Owner controls')).toBeNull();
    expect(canManage('Manager')).toBe(true);
    expect(canManage('Technician')).toBe(false);
  });
});

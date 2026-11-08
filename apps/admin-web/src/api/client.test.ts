import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
const response = (data: unknown, status = 200) =>
  new Response(status === 204 ? null : JSON.stringify(data), { status });
const access = {
  accessToken: 'memory-only-access',
  expiresAt: '2090-01-01T00:00:00Z',
  sessionId: 'session',
  tenantId: 'tenant-a',
  role: 'Manager',
  userId: 'user',
};
beforeEach(() => {
  vi.resetModules();
  vi.stubGlobal('BroadcastChannel', undefined);
});
afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});
describe('secure API transport', () => {
  it('rejects insecure API configuration before sending credentials and keeps the module recoverable', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5080');
    const fetcher = vi.fn();
    vi.stubGlobal('fetch', fetcher);
    const client = await import('./client');
    await expect(client.login({})).rejects.toThrow('HTTPS API origin');
    expect(fetcher).not.toHaveBeenCalled();
  });
  it('uses credentialed CSRF login and keeps tokens out of persistent storage', async () => {
    const storage = vi.spyOn(Storage.prototype, 'setItem');
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response({ token: 'csrf' }))
      .mockResolvedValueOnce(response(access));
    vi.stubGlobal('fetch', fetcher);
    const client = await import('./client');
    await client.login({ email: 'user@example.test', password: 'Strong-Password-42!' });
    expect(fetcher.mock.calls[1]?.[0]).toContain('/auth/web/login');
    const init = fetcher.mock.calls[1]?.[1] as RequestInit;
    expect(init.credentials).toBe('include');
    expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('csrf');
    expect(client.sessionStore.getSnapshot()?.accessToken).toBe(access.accessToken);
    expect(storage).not.toHaveBeenCalled();
    storage.mockRestore();
  });
  it('deduplicates simultaneous refresh operations', async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response({ token: 'csrf' }))
      .mockResolvedValueOnce(response(access));
    vi.stubGlobal('fetch', fetcher);
    const client = await import('./client');
    await Promise.all([client.refresh(), client.refresh(), client.refresh()]);
    expect(fetcher).toHaveBeenCalledTimes(2);
  });
  it('uses a fresh identity-bound CSRF token when switching organizations', async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response({ token: 'anon' }))
      .mockResolvedValueOnce(response(access))
      .mockResolvedValueOnce(response({ token: 'bound' }))
      .mockResolvedValueOnce(response({ ...access, tenantId: 'tenant-b' }));
    vi.stubGlobal('fetch', fetcher);
    const client = await import('./client');
    await client.login({});
    await client.selectOrganization('tenant-b');
    expect(
      new Headers((fetcher.mock.calls[2]?.[1] as RequestInit).headers).get('Authorization'),
    ).toBe(`Bearer ${access.accessToken}`);
    expect(
      new Headers((fetcher.mock.calls[3]?.[1] as RequestInit).headers).get('X-CSRF-TOKEN'),
    ).toBe('bound');
    expect(client.sessionStore.getSnapshot()?.tenantId).toBe('tenant-b');
  });
  it('does not retry a failed write and clears an invalid session', async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response({ token: 'csrf' }))
      .mockResolvedValueOnce(response(access))
      .mockResolvedValueOnce(response({ code: 'session_invalid', detail: 'Sign in again.' }, 401));
    vi.stubGlobal('fetch', fetcher);
    const client = await import('./client');
    await client.login({});
    await expect(
      client.api('/customers', { method: 'POST', body: { name: 'Acme' } }),
    ).rejects.toMatchObject({ status: 401 });
    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(client.sessionStore.getSnapshot()).toBeNull();
  });
  it('retains actionable conflict details without discarding the session', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(response({ token: 'csrf' }))
        .mockResolvedValueOnce(response(access))
        .mockResolvedValueOnce(
          response(
            {
              code: 'work_order_version_conflict',
              detail: 'Reload before retrying.',
              traceId: 'trace',
            },
            409,
          ),
        ),
    );
    const client = await import('./client');
    await client.login({});
    await expect(
      client.api('/work-orders/id', { method: 'PATCH', body: {} }),
    ).rejects.toMatchObject({ status: 409, code: 'work_order_version_conflict', traceId: 'trace' });
    expect(client.sessionStore.getSnapshot()).not.toBeNull();
  });
  it('encodes filters and excludes empty values', async () => {
    const { queryString } = await import('./client');
    expect(queryString({ search: 'a&b', empty: '', missing: undefined, page: 2 })).toBe(
      'search=a%26b&page=2',
    );
  });
  it('discards a late response from the previous organization', async () => {
    let resolveQuery: (value: Response) => void = () => undefined;
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(response({ token: 'csrf' }))
      .mockResolvedValueOnce(response(access))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            resolveQuery = resolve;
          }),
      )
      .mockResolvedValueOnce(response({ token: 'bound' }))
      .mockResolvedValueOnce(response({ ...access, tenantId: 'tenant-b' }));
    vi.stubGlobal('fetch', fetcher);
    const client = await import('./client');
    await client.login({});
    const pending = client.api('/customers');
    await client.selectOrganization('tenant-b');
    resolveQuery(response({ items: [{ name: 'Private tenant A record' }] }));
    await expect(pending).rejects.toMatchObject({ name: 'AbortError' });
  });
});

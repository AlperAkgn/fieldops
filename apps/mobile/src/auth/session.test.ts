import { describe, expect, it, vi } from 'vitest';
import { MobileSession, type CredentialStore } from './session';
import { NativeTransport, ApiError } from '../api/transport';
import { setImmediate } from 'node:timers/promises';
const pair = (overrides = {}) => ({
  accessToken: 'access',
  refreshToken: 'refresh-1',
  expiresAt: '2099-01-01T00:00:00Z',
  refreshExpiresAt: '2099-02-01T00:00:00Z',
  sessionId: 'session',
  tenantId: 'tenant',
  userId: 'user',
  role: 'Technician',
  ...overrides,
});
function setup(
  handler: (path: string, init?: RequestInit) => unknown | Promise<unknown> = () => pair(),
) {
  let value: string | null = null;
  const storage: CredentialStore = {
    get: vi.fn(async () => value),
    set: vi.fn(async (next) => {
      value = next;
    }),
    remove: vi.fn(async () => {
      value = null;
    }),
  };
  const fetcher = vi.fn(async (url, init) => {
    const result = await handler(String(url).split('/api/v1')[1]!, init);
    return result instanceof Response ? result : Response.json(result);
  }) as unknown as typeof fetch;
  const clear = vi.fn();
  const eraseVault = vi.fn(async () => {});
  const start = () =>
    new MobileSession(
      new NativeTransport('https://api.fieldops.test', fetcher),
      storage,
      clear,
      Date.now,
      eraseVault,
    );
  return { session: start(), start, storage, clear, fetcher, eraseVault, value: () => value };
}
describe('protected native sessions', () => {
  it('restores only the protected offline scope, then clears it before an offline restart after sign out', async () => {
    const ctx = setup();
    await ctx.session.login('a', 'b');
    const restarted = ctx.start();
    await restarted.restoreOffline();
    expect(restarted.snapshot()).toMatchObject({
      status: 'offline',
      access: null,
      offline: { tenantId: 'tenant', sessionId: 'session' },
    });
    await expect(restarted.request('/protected')).rejects.toMatchObject({ status: 401 });
    expect(ctx.fetcher).toHaveBeenCalledTimes(1);
    ctx.eraseVault.mockClear();
    expect(await restarted.logout()).toBe(false);
    expect(ctx.eraseVault).toHaveBeenCalledOnce();
    expect(ctx.value()).toBeNull();
    const afterLogout = ctx.start();
    await afterLogout.restoreOffline();
    expect(afterLogout.snapshot()).toMatchObject({
      status: 'signedOut',
      access: null,
      offline: null,
    });
  });

  it('does not preserve the cached key or proof after server revocation on reconnect', async () => {
    const ctx = setup((path) =>
      path === '/auth/refresh' ? new Response('{}', { status: 401 }) : pair(),
    );
    await ctx.session.login('a', 'b');
    const restarted = ctx.start();
    await restarted.restoreOffline();
    ctx.eraseVault.mockClear();
    await expect(restarted.refresh()).rejects.toMatchObject({ status: 401 });
    expect(restarted.snapshot()).toMatchObject({
      status: 'signedOut',
      access: null,
      offline: null,
    });
    expect(ctx.eraseVault).toHaveBeenCalledOnce();
    expect(ctx.value()).toBeNull();
  });

  it('erases the previous workspace before storing the new scope and ignores revocation for the old scope', async () => {
    const ctx = setup((path) =>
      path.endsWith('/select') ? pair({ tenantId: 'other', sessionId: 'new' }) : pair(),
    );
    await ctx.session.login('a', 'b');
    const previous = ctx.session.snapshot().access!;
    ctx.eraseVault.mockClear();
    await ctx.session.selectOrganization('other');
    expect(ctx.eraseVault).toHaveBeenCalledOnce();
    await ctx.session.revokeOfflineScope(previous);
    expect(ctx.eraseVault).toHaveBeenCalledOnce();
    const restarted = ctx.start();
    await restarted.restoreOffline();
    expect(restarted.snapshot().offline).toMatchObject({ tenantId: 'other', sessionId: 'new' });
  });
  it('clears protected proof before a delayed server logout can be interrupted', async () => {
    let entered!: () => void;
    let release!: () => void;
    const sent = new Promise<void>((resolve) => {
      entered = resolve;
    });
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const ctx = setup(async (path) => {
      if (path === '/auth/logout') {
        entered();
        await gate;
        return new Response(null, { status: 204 });
      }
      return pair();
    });
    await ctx.session.login('a', 'b');
    const exiting = ctx.session.logout();
    await sent;
    const clearedBeforeNetwork = ctx.value() === null;
    release();
    await exiting;
    expect(clearedBeforeNetwork).toBe(true);
  });
  it('does not erase a successful workspace selection for an old in-flight 401', async () => {
    let deny!: (value: Response) => void;
    let select!: (value: unknown) => void;
    let entered!: () => void;
    const selecting = new Promise<void>((resolve) => {
      entered = resolve;
    });
    const ctx = setup((path) => {
      if (path === '/protected')
        return new Promise<Response>((resolve) => {
          deny = resolve;
        });
      if (path.endsWith('/select')) {
        entered();
        return new Promise((resolve) => {
          select = resolve;
        });
      }
      return pair();
    });
    await ctx.session.login('a', 'b');
    const read = ctx.session.request('/protected').catch((error) => error);
    const selection = ctx.session.selectOrganization('other');
    await selecting;
    deny(new Response('{}', { status: 401 }));
    await setImmediate();
    select(pair({ tenantId: 'other', sessionId: 'next-session' }));
    await selection;
    await read;
    expect(ctx.session.snapshot().access?.tenantId).toBe('other');
  });
  it('waits for an active rotation before sending protected requests', async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const ctx = setup(async (path) => {
      if (path === '/auth/refresh') {
        await gate;
        return pair({ accessToken: 'new-access' });
      }
      return path === '/auth/login' ? pair() : { ok: true };
    });
    await ctx.session.login('a', 'b');
    const rotation = ctx.session.refresh();
    const read = ctx.session.request('/protected');
    release();
    await rotation;
    expect(await read).toEqual({ ok: true });
    expect(vi.mocked(ctx.fetcher).mock.calls.at(-1)?.[1]?.headers).toMatchObject({
      Authorization: 'Bearer new-access',
    });
  });
  it('does not send expired stored refresh credentials to the API', async () => {
    const ctx = setup();
    await ctx.storage.set(
      JSON.stringify({
        version: 1,
        origin: 'https://api.fieldops.test',
        state: 'ready',
        refreshToken: 'old',
        refreshExpiresAt: '2000-01-01',
      }),
    );
    await ctx.session.bootstrap();
    expect(ctx.fetcher).not.toHaveBeenCalled();
    expect(ctx.value()).toBeNull();
  });
  it('clears credential and scope after interrupted workspace rotation', async () => {
    const ctx = setup((path) => {
      if (path.endsWith('/select')) throw new Error('connection interrupted');
      return pair();
    });
    await ctx.session.login('a', 'b');
    await expect(ctx.session.selectOrganization('other')).rejects.toThrow();
    expect(ctx.session.snapshot().status).toBe('signedOut');
    expect(ctx.value()).toBeNull();
  });
  it('persists only refresh proof and keeps access tokens out of storage', async () => {
    const ctx = setup();
    await ctx.session.login(' user@test ', 'secret');
    expect(ctx.session.snapshot().access?.role).toBe('Technician');
    expect(ctx.value()).not.toContain('accessToken');
    expect(ctx.value()).not.toContain('secret');
    expect(JSON.parse(ctx.value()!).refreshToken).toBe('refresh-1');
  });
  it('deduplicates refresh, writes rotating marker first, and persists the new credential', async () => {
    let release!: () => void;
    const gate = new Promise<void>((r) => {
      release = r;
    });
    const ctx = setup(async (path) => {
      if (path === '/auth/refresh') {
        expect(JSON.parse(ctx.value()!).state).toBe('rotating');
        await gate;
        return pair({ refreshToken: 'refresh-2' });
      }
      return pair();
    });
    await ctx.session.login('a', 'b');
    const a = ctx.session.refresh();
    const b = ctx.session.refresh();
    expect(a).toBe(b);
    release();
    await Promise.all([a, b]);
    expect(JSON.parse(ctx.value()!).refreshToken).toBe('refresh-2');
    expect(
      vi.mocked(ctx.fetcher).mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh')),
    ).toHaveLength(1);
  });
  it.each(['rotating', 'signedOut'])(
    'never replays a %s credential after interruption',
    async (state) => {
      const ctx = setup();
      await ctx.storage.set(
        JSON.stringify({
          version: 1,
          origin: 'https://api.fieldops.test',
          state,
          refreshToken: 'old',
        }),
      );
      await ctx.session.bootstrap();
      expect(ctx.fetcher).not.toHaveBeenCalled();
      expect(ctx.session.snapshot().status).toBe('signedOut');
    },
  );
  it('clears ambiguous refresh instead of retrying a consumed token', async () => {
    const ctx = setup((path) => {
      if (path === '/auth/refresh') throw new TypeError('network lost');
      return pair();
    });
    await ctx.session.login('a', 'b');
    await expect(ctx.session.refresh()).rejects.toThrow('network lost');
    expect(ctx.value()).toBeNull();
    expect(ctx.session.snapshot().access).toBeNull();
  });
  it('fails closed if storing a successfully rotated token fails', async () => {
    const ctx = setup();
    await ctx.session.login('a', 'b');
    vi.mocked(ctx.storage.set)
      .mockImplementationOnce(async () => undefined)
      .mockRejectedValueOnce(new Error('disk'));
    await expect(ctx.session.refresh()).rejects.toThrow();
    expect(ctx.session.snapshot().access).toBeNull();
    expect(ctx.value()).toBeNull();
  });
  it('locks access when both deletion and tombstone fail', async () => {
    const ctx = setup();
    await ctx.session.login('a', 'b');
    vi.mocked(ctx.storage.set).mockRejectedValue(new Error('locked'));
    vi.mocked(ctx.storage.remove).mockRejectedValue(new Error('locked'));
    await expect(ctx.session.logout()).rejects.toThrow();
    expect(ctx.session.snapshot().status).toBe('locked');
    expect(ctx.session.snapshot().access).toBeNull();
  });
  it('clears local credentials and caches even when server logout is unavailable', async () => {
    const ctx = setup((path) => {
      if (path === '/auth/logout') throw new Error('offline');
      return pair();
    });
    await ctx.session.login('a', 'b');
    expect(await ctx.session.logout()).toBe(false);
    expect(ctx.value()).toBeNull();
    expect(ctx.clear).toHaveBeenCalled();
    expect(ctx.session.snapshot().message).toContain('could not be confirmed');
  });
  it('drops a late response after workspace selection', async () => {
    let release!: (result: unknown) => void;
    const ctx = setup((path) =>
      path === '/work-orders'
        ? new Promise((resolve) => {
            release = resolve;
          })
        : pair({ tenantId: path.endsWith('/select') ? 'other' : 'tenant' }),
    );
    await ctx.session.login('a', 'b');
    const late = ctx.session.request('/work-orders');
    await Promise.resolve();
    await ctx.session.selectOrganization('other');
    release({ items: ['old tenant data'] });
    await expect(late).rejects.toMatchObject({ code: 'scope_changed' });
    expect(ctx.session.snapshot().access?.tenantId).toBe('other');
  });
  it('does not retry a rejected mutation and erases revoked sessions', async () => {
    const ctx = setup((path) =>
      path === '/auth/login'
        ? pair()
        : new Response(JSON.stringify({ detail: 'Revoked' }), { status: 401 }),
    );
    await ctx.session.login('a', 'b');
    await expect(
      ctx.session.request('/work-orders/id/start', {
        method: 'POST',
        body: { expectedVersion: 1 },
      }),
    ).rejects.toBeInstanceOf(ApiError);
    expect(ctx.fetcher).toHaveBeenCalledTimes(2);
    expect(ctx.session.snapshot().access).toBeNull();
  });
  it('never restores credentials stored for another API origin', async () => {
    const ctx = setup();
    await ctx.storage.set(
      JSON.stringify({
        version: 1,
        origin: 'https://other.test',
        state: 'ready',
        refreshToken: 'old',
        refreshExpiresAt: '2099-01-01',
      }),
    );
    await ctx.session.bootstrap();
    expect(ctx.fetcher).not.toHaveBeenCalled();
  });
  it('rejects malformed login results without leaving credentials', async () => {
    const ctx = setup(() => ({ ...pair(), role: 'Administrator' }));
    await expect(ctx.session.login('a', 'b')).rejects.toThrow('Invalid session');
    expect(ctx.value()).toBeNull();
  });
  it('updates role and clears cached scope after authoritative profile validation', async () => {
    const ctx = setup((path) => (path === '/auth/me' ? { id: 'user', role: 'Viewer' } : pair()));
    await ctx.session.login('a', 'b');
    const count = ctx.clear.mock.calls.length;
    await ctx.session.profile();
    expect(ctx.clear.mock.calls.length).toBe(count + 1);
    expect(ctx.session.snapshot().access?.role).toBe('Viewer');
  });
});

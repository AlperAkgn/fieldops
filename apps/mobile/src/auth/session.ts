import { parseTokenPair, type Profile, type TokenPair } from '../api/contracts';
import { ApiError, NativeTransport, type RequestOptions } from '../api/transport';
export interface CredentialStore {
  get(): Promise<string | null>;
  set(value: string): Promise<void>;
  remove(): Promise<void>;
}
export type SessionState = {
  status: 'loading' | 'signedOut' | 'authenticated' | 'offline' | 'locked';
  access: Omit<TokenPair, 'refreshToken'> | null;
  offline?: OfflineIdentity | null;
  message: string | null;
  busy: boolean;
};
export type OfflineIdentity = Pick<
  TokenPair,
  'tenantId' | 'userId' | 'sessionId' | 'role' | 'refreshExpiresAt'
>;
export function sameOfflineScope(left: OfflineIdentity | null | undefined, right: OfflineIdentity) {
  return (
    left?.tenantId === right.tenantId &&
    left.userId === right.userId &&
    left.sessionId === right.sessionId &&
    left.role === right.role
  );
}
type Stored = {
  version: 1;
  origin: string;
  state: 'ready';
  refreshToken: string;
  refreshExpiresAt: string;
  offline?: OfflineIdentity;
};
export class MobileSession {
  private state: SessionState = {
    status: 'loading',
    access: null,
    message: null,
    busy: false,
  };
  private listeners = new Set<() => void>();
  private tail: Promise<unknown> = Promise.resolve();
  private refreshing: Promise<void> | null = null;
  private booting: Promise<void> | null = null;
  private generation = 0;
  constructor(
    readonly transport: NativeTransport,
    private storage: CredentialStore,
    private clearScope: () => void,
    private now: () => number = Date.now,
    private removeOffline: () => Promise<void> = async () => {},
  ) {}
  snapshot = () => this.state;
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };
  private publish(next: Partial<SessionState>) {
    this.state = { ...this.state, ...next };
    this.listeners.forEach((listener) => listener());
  }
  private queue<T>(operation: () => Promise<T>): Promise<T> {
    const next = this.tail.then(operation, operation);
    this.tail = next.catch(() => undefined);
    return next;
  }
  private reset(status: SessionState['status'], message: string | null) {
    this.generation++;
    this.clearScope();
    this.publish({ status, access: null, offline: null, message });
  }
  private async erase(message: string | null = null) {
    this.reset('signedOut', message);
    let cleared = false;
    try {
      await this.storage.set(
        JSON.stringify({
          version: 1,
          origin: this.transport.origin,
          state: 'signedOut',
        }),
      );
      cleared = true;
    } catch {
      /* Try deletion if tombstone storage fails. */
    }
    try {
      await this.storage.remove();
      cleared = true;
    } catch {
      /* A persisted tombstone also prevents bootstrap. */
    }
    try {
      await this.removeOffline();
    } catch {
      cleared = false;
    }
    if (!cleared) {
      this.reset(
        'locked',
        'Protected storage could not be cleared. Retry secure sign out before using this device.',
      );
      throw new Error('Could not clear protected session storage.');
    }
  }
  private async credential(): Promise<Stored | null> {
    const value = await this.storage.get();
    if (!value) return null;
    const record = JSON.parse(value) as Partial<Stored>;
    if (
      record.version !== 1 ||
      record.origin !== this.transport.origin ||
      record.state !== 'ready' ||
      typeof record.refreshToken !== 'string' ||
      !record.refreshToken ||
      record.refreshToken.length > 2000 ||
      typeof record.refreshExpiresAt !== 'string' ||
      Date.parse(record.refreshExpiresAt) <= this.now() ||
      !Number.isFinite(Date.parse(record.refreshExpiresAt))
    )
      return null;
    return record as Stored;
  }
  private async accept(value: unknown, previous?: OfflineIdentity) {
    const pair = parseTokenPair(value);
    const { sessionId, userId, tenantId, role, refreshExpiresAt } = pair;
    const offline = { sessionId, userId, tenantId, role, refreshExpiresAt };
    if (previous && !sameOfflineScope(previous, offline)) await this.removeOffline();
    await this.storage.set(
      JSON.stringify({
        version: 1,
        origin: this.transport.origin,
        state: 'ready',
        refreshToken: pair.refreshToken,
        refreshExpiresAt: pair.refreshExpiresAt,
        offline,
      } satisfies Stored),
    );
    const access = {
      accessToken: pair.accessToken,
      expiresAt: pair.expiresAt,
      refreshExpiresAt: pair.refreshExpiresAt,
      sessionId: pair.sessionId,
      tenantId: pair.tenantId,
      userId: pair.userId,
      role: pair.role,
    };
    const old = this.state.access;
    if (
      !old ||
      old.userId !== access.userId ||
      old.tenantId !== access.tenantId ||
      old.role !== access.role ||
      old.sessionId !== access.sessionId
    ) {
      this.generation++;
      this.clearScope();
    }
    this.publish({ status: 'authenticated', access, offline: null, message: null });
  }
  private async refreshInside() {
    this.publish({ busy: true });
    try {
      const credential = await this.credential();
      if (!credential) {
        await this.erase();
        return;
      }
      // Persist a marker BEFORE consuming a one-use credential. After a crash or
      // ambiguous response, require login instead of replaying an old refresh token.
      await this.storage.set(
        JSON.stringify({
          version: 1,
          origin: this.transport.origin,
          state: 'rotating',
        }),
      );
      const pair = await this.transport.request('/auth/refresh', {
        method: 'POST',
        body: { refreshToken: credential.refreshToken },
      });
      await this.accept(pair, credential.offline);
    } catch (error) {
      await this.erase('Your session could not be restored safely. Please sign in again.');
      throw error;
    } finally {
      this.publish({ busy: false });
    }
  }
  bootstrap() {
    if (!this.booting) this.booting = this.refresh();
    return this.booting;
  }
  restoreOffline() {
    return this.queue(async () => {
      if (this.state.status !== 'loading') return;
      const credential = await this.credential().catch(() => null);
      if (!credential) {
        await this.erase('Connect to the service to sign in.');
        return;
      }
      const scope = credential?.offline;
      if (
        !this.transport.origin.startsWith('https://') ||
        scope?.role !== 'Technician' ||
        !scope.sessionId ||
        !scope.userId ||
        !scope.tenantId ||
        scope.refreshExpiresAt !== credential?.refreshExpiresAt ||
        Date.parse(scope.refreshExpiresAt) <= this.now()
      ) {
        // Keep a valid credential available for a later online boot.
        this.reset('signedOut', 'Connect to the service to sign in.');
        return;
      }
      this.publish({ status: 'offline', access: null, offline: scope, message: null });
    });
  }
  refresh() {
    if (!this.refreshing)
      this.refreshing = this.queue(() => this.refreshInside()).finally(() => {
        this.refreshing = null;
      });
    return this.refreshing;
  }
  login(email: string, password: string) {
    return this.queue(async () => {
      this.publish({ busy: true });
      try {
        await this.erase();
        const result = await this.transport.request('/auth/login', {
          method: 'POST',
          body: { email: email.trim(), password },
        });
        await this.accept(result);
      } catch (error) {
        await this.erase();
        throw error;
      } finally {
        this.publish({ busy: false });
      }
    });
  }
  selectOrganization(id: string) {
    return this.queue(async () => {
      const access = this.state.access;
      if (!access) throw new ApiError(401, 'session_required', 'Sign in again.');
      this.publish({ busy: true });
      try {
        const credential = await this.credential();
        if (!credential) throw new Error('Session unavailable.');
        await this.removeOffline();
        await this.storage.set(
          JSON.stringify({
            version: 1,
            origin: this.transport.origin,
            state: 'rotating',
          }),
        );
        await this.accept(
          await this.transport.request(`/organizations/${encodeURIComponent(id)}/select`, {
            method: 'POST',
            token: access.accessToken,
            body: { refreshToken: credential.refreshToken },
          }),
        );
      } catch (error) {
        await this.erase('Workspace selection was interrupted. Please sign in again.');
        throw error;
      } finally {
        this.publish({ busy: false });
      }
    });
  }
  logout() {
    return this.queue(async () => {
      this.publish({ busy: true });
      const access = this.state.access;
      let online = false;
      let cleanupFailure: { error: unknown } | null = null;
      try {
        // Persist sign-out intent before any network wait. A killed/offline app
        // must not bootstrap the old proof after the user has signed out.
        try {
          await this.erase();
        } catch (error) {
          cleanupFailure = { error };
        }
        try {
          if (access) {
            await this.transport.request('/auth/logout', {
              method: 'POST',
              token: access.accessToken,
            });
            online = true;
          }
        } catch {
          /* Local cleanup does not depend on server availability. */
        }
        if (cleanupFailure) throw cleanupFailure.error;
        this.publish({
          message: online
            ? null
            : 'Signed out on this device. Server revocation could not be confirmed.',
        });
        return online;
      } finally {
        this.publish({ busy: false });
      }
    });
  }
  clearLocal() {
    return this.queue(() => this.erase());
  }
  revokeOfflineScope(scope: OfflineIdentity) {
    return this.queue(async () => {
      if (sameOfflineScope(this.state.access ?? this.state.offline, scope))
        await this.erase('Your workspace access changed. Sign in again.');
    });
  }
  async request<T>(path: string, options: Omit<RequestOptions, 'token'> = {}): Promise<T> {
    if (this.refreshing) await this.refreshing;
    if (this.state.busy)
      throw new ApiError(0, 'session_busy', 'Wait for the session operation to finish.');
    if (this.state.access && Date.parse(this.state.access.expiresAt) <= this.now() + 30000)
      await this.refresh();
    const access = this.state.access;
    const generation = this.generation;
    if (!access) throw new ApiError(401, 'session_required', 'Please sign in again.');
    try {
      const result = await this.transport.request<T>(path, {
        ...options,
        token: access.accessToken,
      });
      if (generation !== this.generation)
        throw new ApiError(0, 'scope_changed', 'Workspace changed. Reload current data.');
      return result;
    } catch (error) {
      if (error instanceof ApiError && error.status === 401 && generation === this.generation)
        await this.queue(async () => {
          if (generation === this.generation)
            await this.erase('Your session expired or was revoked. Sign in again.');
        });
      throw error;
    }
  }
  async profile(signal?: AbortSignal) {
    const profile = await this.request<Profile>('/auth/me', signal ? { signal } : {});
    const access = this.state.access;
    if (access && profile.role !== access.role) {
      this.generation++;
      this.clearScope();
      this.publish({ access: { ...access, role: profile.role } });
      await this.queue(async () => {
        const current = this.state.access;
        if (!current || !sameOfflineScope(current, { ...access, role: profile.role })) return;
        const stored = await this.credential();
        if (stored) await this.storage.set(JSON.stringify({ ...stored, offline: undefined }));
        await this.removeOffline();
      });
    }
    return profile;
  }
}

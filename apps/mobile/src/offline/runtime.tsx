import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type PropsWithChildren,
} from 'react';
import { AppState, Platform } from 'react-native';
import * as Network from 'expo-network';
import { randomUUID } from 'expo-crypto';
import { useQueryClient } from '@tanstack/react-query';
import { useSession } from '../auth/runtime';
import { ApiError } from '../api/transport';
import { fetchHealth } from '../api/health';
import { sameOfflineScope, type OfflineIdentity } from '../auth/session';
import { SyncEngine, type PushResult } from './engine';
import {
  offlineWindow,
  type Intent,
  type OfflineSnapshot,
  type OfflineStore,
  type PullPage,
} from './store';
import { openOfflineVault } from './vault';

type State = {
  offline: boolean;
  busy: boolean;
  snapshot: OfflineSnapshot | null;
  error: string | null;
};
const initialState: State = {
  offline: false,
  busy: false,
  snapshot: null,
  error: null,
};
const maxBackoff = 5 * 60_000;

class OfflineWorkspace {
  private state = initialState;
  private listeners = new Set<() => void>();
  private store: OfflineStore | null = null;
  private engine: SyncEngine | null = null;
  private connected = true;
  private foreground = true;
  private disposed = false;
  private syncing = false;
  private retryUntil = 0;
  private failures = 0;
  private lastRun = 0;
  private opening = false;

  constructor(
    private session: ReturnType<typeof useSession>['session'],
    private identity: OfflineIdentity,
    private reconcile: () => void,
    private cancelReads: () => void,
  ) {}

  snapshot = () => this.state;
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };
  private publish(patch: Partial<State>) {
    if (!this.active()) return;
    this.state = { ...this.state, ...patch };
    this.listeners.forEach((listener) => listener());
  }

  private active = () => {
    const session = this.session.snapshot();
    return (
      !this.disposed &&
      !session.busy &&
      sameOfflineScope(session.access ?? session.offline, this.identity)
    );
  };

  async start() {
    if (!this.active() || this.store || this.opening) return;
    this.opening = true;
    const lifecycle = this.lifecycle;
    const active = () => this.active() && this.lifecycle === lifecycle;
    try {
      const connection = await Network.getNetworkStateAsync();
      if (!active()) return;
      this.connected = connection.isConnected !== false && connection.isInternetReachable !== false;
      const onlineSession = this.session.snapshot().status === 'authenticated';
      const store = await openOfflineVault(
        this.session.transport.origin,
        this.identity,
        onlineSession,
        active,
      );
      if (!active()) return;
      this.store = store;
      if (!store) {
        this.publish({
          offline: !this.connected,
          error: 'Connect and download work orders before using offline mode.',
        });
        return;
      }
      this.engine = new SyncEngine(store, {
        pull: (cursor) =>
          this.session.request<PullPage>(
            `/sync/pull?protocol=1${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`,
          ),
        push: async (command) => {
          const result = await this.session.request<{ results: PushResult[] }>('/sync/push', {
            method: 'POST',
            body: { protocol: 1, operations: [command] },
          });
          if (result.results.length !== 1 || !result.results[0])
            throw new Error('Invalid synchronization response.');
          return result.results[0];
        },
      });
      this.publish({ offline: !this.connected || !onlineSession });
      await this.refreshView();
      void this.sync();
    } catch {
      if (active())
        this.publish({
          error: 'Encrypted offline storage is unavailable. Use a compatible native build.',
        });
    } finally {
      this.opening = false;
    }
  }

  private lifecycle = 0;
  resume() {
    this.disposed = false;
    void this.start();
  }

  stop() {
    this.disposed = true;
    this.lifecycle++;
  }

  network(connected: boolean) {
    const recovered = !this.connected && connected;
    this.connected = connected;
    if (!connected) {
      if (this.active()) this.cancelReads();
      this.publish({ offline: true });
    } else if (recovered) {
      this.retryUntil = 0;
      void this.sync();
    }
  }

  app(active: boolean) {
    this.foreground = active;
    if (active) void this.sync();
  }

  tick() {
    if (!this.active()) return;
    if (!this.store && !this.state.error) void this.start();
    const cache = this.state.snapshot;
    if (cache?.lastSync != null && !this.validLease(cache.lastSync)) {
      this.publish({ snapshot: null, error: 'Offline access expired. Connect to renew access.' });
    }
    if (Date.now() - this.lastRun >= 30_000) void this.sync();
  }

  private validLease(lastSync: number) {
    const now = Date.now();
    const session = this.session.snapshot();
    return (
      now >= lastSync &&
      now - lastSync < offlineWindow &&
      now < Date.parse((session.access ?? session.offline ?? this.identity).refreshExpiresAt)
    );
  }

  private refreshView = async () => {
    if (!this.store || !this.active()) return;
    const snapshot = await this.store.snapshot();
    const changedAccess =
      snapshot.rebuilding || snapshot.removed > (this.state.snapshot?.removed ?? 0);
    this.publish({
      snapshot:
        snapshot.lastSync === null || !this.validLease(snapshot.lastSync)
          ? { ...snapshot, orders: [], operations: [] }
          : snapshot.rebuilding
            ? { ...snapshot, operations: [] }
            : snapshot,
    });
    if (changedAccess && this.active()) this.cancelReads();
  };

  sync = async () => {
    if (
      !this.active() ||
      !this.connected ||
      !this.foreground ||
      this.syncing ||
      Date.now() < this.retryUntil
    )
      return;
    const previous = this.session.snapshot();
    if (!this.engine && previous.status !== 'offline') return;
    this.syncing = true;
    this.lastRun = Date.now();
    this.publish({ busy: true });
    try {
      // Check service reachability before consuming a rotating refresh credential.
      if (
        previous.status === 'offline' ||
        (previous.access && Date.parse(previous.access.expiresAt) <= Date.now() + 30000)
      ) {
        const health = await fetchHealth(this.session.transport.origin, 'ready');
        if (health.status !== 'Healthy') throw new Error('API unavailable.');
        if (!this.active() || !this.connected || !this.foreground) return;
        await this.session.refresh();
      }
      if (!this.active() || this.session.snapshot().status !== 'authenticated') return;
      if (this.engine) {
        await this.engine.run(
          () => this.active() && this.connected && this.foreground,
          this.refreshView,
        );
        const rateLimit = this.state.snapshot?.retryAt ?? 0;
        if (rateLimit > Date.now()) return;
      } else {
        await this.start();
        if (!this.engine) return;
      }
      this.failures = 0;
      this.retryUntil = 0;
      this.publish({ offline: !this.connected, error: null });
      if (this.connected && this.active()) this.reconcile();
    } catch (error) {
      if (!this.active()) return;
      if (error instanceof ApiError && [401, 403].includes(error.status)) {
        this.publish({
          snapshot: null,
          error: 'Your offline workspace access changed.',
        });
        this.cancelReads();
        await this.session.revokeOfflineScope(this.identity).catch(() => undefined);
        this.store = null;
        this.engine = null;
        return;
      }
      this.failures++;
      const delay =
        Math.min(maxBackoff, 1000 * 2 ** Math.min(this.failures, 8)) * (0.5 + Math.random() * 0.5);
      this.retryUntil =
        Date.now() +
        Math.max(delay, error instanceof ApiError ? (error.retryAfter ?? 0) * 1000 : 0);
      const unavailable = !(error instanceof ApiError) || error.status === 0 || error.status >= 500;
      this.publish({
        offline: unavailable || !this.connected,
        error: 'Synchronization paused. Saved edits will retry when the service is available.',
      });
    } finally {
      this.syncing = false;
      this.publish({ busy: false });
    }
  };

  enqueue = async (orderId: string, version: number, intent: Intent) => {
    const lastSync = this.state.snapshot?.lastSync;
    if (!this.store || !this.active() || lastSync == null || !this.validLease(lastSync))
      throw new Error('Connect and download work orders before saving offline.');
    await this.store.enqueue(randomUUID(), orderId, version, intent);
    await this.refreshView();
    void this.sync();
  };

  discard = async (id: string) => {
    if (!this.store || !this.active() || this.syncing)
      throw new Error('Wait for synchronization to finish.');
    await this.store.discard(id);
    await this.refreshView();
  };

  resolve = async (id: string, reviewedVersion: number) => {
    const lastSync = this.state.snapshot?.lastSync;
    if (
      !this.store ||
      !this.active() ||
      this.syncing ||
      this.state.offline ||
      lastSync == null ||
      !this.validLease(lastSync)
    )
      throw new Error('Connect and refresh before applying a conflict resolution.');
    await this.store.resolve(id, randomUUID(), reviewedVersion);
    await this.refreshView();
    void this.sync();
  };
}

const Context = createContext<OfflineWorkspace | null>(null);
const emptySubscribe = () => () => {};
const emptySnapshot = () => initialState;
export function useOffline() {
  const { state: session } = useSession();
  const workspace = useContext(Context);
  const state = useSyncExternalStore(
    workspace?.subscribe ?? emptySubscribe,
    workspace?.snapshot ?? emptySnapshot,
  );
  return { workspace, ...state, offline: state.offline || session.status === 'offline' };
}

export function OfflineProvider({ children }: PropsWithChildren) {
  const { session, state } = useSession();
  const identity = state.access?.role === 'Technician' ? state.access : state.offline;
  const key = identity
    ? JSON.stringify([identity.tenantId, identity.userId, identity.sessionId, identity.role])
    : null;
  if (!key || !identity || Platform.OS === 'web') return children;
  return (
    <WorkspaceProvider key={key} identity={identity} session={session}>
      {children}
    </WorkspaceProvider>
  );
}

function WorkspaceProvider({
  identity,
  session,
  children,
}: PropsWithChildren<{
  identity: OfflineIdentity;
  session: ReturnType<typeof useSession>['session'];
}>) {
  const client = useQueryClient();
  const [workspace] = useState(
    () =>
      new OfflineWorkspace(
        session,
        identity,
        () => {
          void client.invalidateQueries({ queryKey: ['mobile'] });
        },
        () => {
          void client.cancelQueries({ queryKey: ['mobile'] });
          client.removeQueries({ queryKey: ['mobile'] });
        },
      ),
  );
  const active = useMemo(() => ({ workspace }), [workspace]);
  useEffect(() => {
    const instance = active.workspace;
    instance.resume();
    const network = Network.addNetworkStateListener((value) =>
      instance.network(value.isConnected !== false && value.isInternetReachable !== false),
    );
    const app = AppState.addEventListener('change', (value) => instance.app(value === 'active'));
    const timer = setInterval(() => instance.tick(), 5000);
    return () => {
      instance.stop();
      network.remove();
      app.remove();
      clearInterval(timer);
    };
  }, [active]);
  return <Context.Provider value={workspace}>{children}</Context.Provider>;
}

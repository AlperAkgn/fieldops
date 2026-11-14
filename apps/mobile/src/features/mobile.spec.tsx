import { describe, expect, it, jest, beforeEach, afterEach } from '@jest/globals';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { QueryClient, QueryClientProvider, notifyManager } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SessionContext } from '../auth/runtime';
import { MobileSession } from '../auth/session';
import { NativeTransport } from '../api/transport';
import SignIn from '../app/sign-in';
import { WorkOrderScreen } from './WorkOrderScreen';
import { OrdersScreen } from './OrdersScreen';
import { withOrderLock } from './queries';
import Profile from '../app/(tabs)/profile';
import { Linking } from 'react-native';
import * as Network from 'expo-network';
import { OfflineProvider } from '../offline/runtime';
import { openOfflineVault } from '../offline/vault';
import type { OfflineSnapshot, OfflineStore } from '../offline/store';
import WorkOrderRoute from '../app/work-orders/[id]';
jest.mock('expo-crypto', () => ({ randomUUID: () => '9f08cfe4-4811-41ca-983e-b6333994bf04' }));
jest.mock('expo-network', () => ({
  getNetworkStateAsync: jest.fn(),
  addNetworkStateListener: jest.fn(),
}));
jest.mock('../offline/vault', () => ({
  openOfflineVault: jest.fn(),
  removeOfflineVault: jest.fn<() => Promise<void>>().mockResolvedValue(undefined),
}));
jest.mock('expo-router', () => ({
  Redirect: () => null,
  useLocalSearchParams: () => ({ id: 'order' }),
  router: {
    push: jest.fn(),
    navigate: jest.fn(),
    canGoBack: () => false,
    replace: jest.fn(),
  },
}));
jest.mock('@expo/vector-icons/Ionicons', () => () => null);
const token = {
  accessToken: 'access',
  refreshToken: 'refresh',
  expiresAt: '2099-01-01',
  refreshExpiresAt: '2099-02-01',
  sessionId: 'session',
  tenantId: 'tenant',
  role: 'Technician',
  userId: 'tech',
};
const initialOrder = {
  id: 'order',
  title: 'Inspect pump',
  workOrderNumber: 'WO-1',
  status: 'Assigned',
  version: 5,
  priority: 'High',
  plannedStartAt: '2026-11-08T10:00:00Z',
  plannedEndAt: '2026-11-08T11:00:00Z',
  actualStartAt: null as string | null,
  actualEndAt: null as string | null,
  description: 'Pressure check',
};
const clients: QueryClient[] = [];
notifyManager.setNotifyFunction((callback) => {
  act(callback);
});
afterEach(() => {
  clients.forEach((client) => client.clear());
  clients.length = 0;
});
let order = { ...initialOrder };
let completed = false;
let rejectWrite = false;
let rejectLogin = false;
let writeGate: Promise<void> | null = null;
let unassigned = false;
let calls: { path: string; body: Record<string, unknown> }[];
function response(value: unknown, status = 200) {
  return {
    ok: status < 400,
    status,
    headers: new Headers(),
    json: async () => value,
  } as Response;
}
function fixture() {
  let stored: string | null = null;
  const fetcher: typeof fetch = async (url, init) => {
    const path = String(url).split('/api/v1')[1]!;
    const body = init?.body ? JSON.parse(String(init.body)) : {};
    calls.push({ path, body });
    if (path === '/auth/login' && rejectLogin)
      return response({ detail: 'Invalid credentials' }, 401);
    if (path === '/auth/login' || path === '/auth/refresh') return response(token);
    if (path === '/auth/me')
      return response({
        id: 'tech',
        displayName: 'Jamie',
        email: 'tech@test',
        role: 'Technician',
        activeOrganizationId: 'tenant',
        emailConfirmed: true,
      });
    if (path === '/auth/logout') return response(null, 204);
    if (path.startsWith('/sync/pull'))
      return response({
        cursor: 'cursor',
        hasMore: false,
        serverTime: new Date().toISOString(),
        orders: [],
      });
    if (path === '/organizations')
      return response([
        {
          id: 'tenant',
          name: 'Field Service',
          role: 'Technician',
          timeZoneId: 'UTC',
        },
      ]);
    if (path.startsWith('/work-orders?'))
      return response({ items: [order], page: 1, pageSize: 20, totalCount: 1 });
    if (unassigned) return response({ detail: 'Not found' }, 404);
    if (init?.method && init.method !== 'GET') {
      if (writeGate) await writeGate;
      if (rejectWrite)
        return response({ detail: 'Changed by another user', code: 'version_conflict' }, 409);
      if (path.endsWith('/start'))
        order = {
          ...order,
          version: order.version + 1,
          status: 'InProgress',
          actualStartAt: '2026-11-08T10:00:00Z',
        };
      if (path.includes('/checklist/')) {
        completed = !!body.isCompleted;
        order.version++;
      }
      if (path.endsWith('/comments')) {
        order.version++;
        return response({
          workOrderVersion: order.version,
          comment: { id: 'note', body: body.body },
        });
      }
      if (path.includes('/checklist/')) return response(checklist());
      return response(order);
    }
    if (path.endsWith('/checklist')) return response(checklist());
    if (path.includes('/comments?') || path.includes('/history?'))
      return response({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    return response({
      order,
      customer: { id: 'customer', name: 'Acme' },
      serviceLocation: {
        id: 'site',
        name: 'Plant',
        addressLine: '12 Main Street',
        city: 'London',
        country: 'UK',
        latitude: null,
        longitude: null,
      },
      asset: { id: 'pump', assetCode: 'P1', name: 'Pump' },
      assignments: [
        {
          id: 'assignment',
          technicianUserId: 'tech',
          displayName: 'Jamie',
          isPrimary: true,
        },
      ],
    });
  };
  function checklist() {
    return {
      workOrderId: 'order',
      workOrderVersion: order.version,
      totalItems: 1,
      completedItems: completed ? 1 : 0,
      progressPercent: completed ? 100 : 0,
      requiredItemsComplete: completed,
      items: [
        {
          id: 'item',
          label: 'Safety check',
          isRequired: true,
          isCompleted: completed,
        },
      ],
    };
  }
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: Infinity },
      mutations: { retry: false, gcTime: Infinity },
    },
  });
  clients.push(client);
  const session = new MobileSession(
    new NativeTransport('https://api.test', fetcher),
    {
      get: async () => stored,
      set: async (value) => {
        stored = value;
      },
      remove: async () => {
        stored = null;
      },
    },
    () => client.clear(),
  );
  function mount(ui: React.ReactNode) {
    return render(
      <SafeAreaProvider
        initialMetrics={{
          frame: { x: 0, y: 0, width: 390, height: 844 },
          insets: { top: 0, left: 0, right: 0, bottom: 0 },
        }}
      >
        <QueryClientProvider client={client}>
          <SessionContext.Provider value={session}>{ui}</SessionContext.Provider>
        </QueryClientProvider>
      </SafeAreaProvider>,
    );
  }
  return { session, client, mount };
}
beforeEach(() => {
  order = { ...initialOrder };
  completed = false;
  rejectWrite = false;
  rejectLogin = false;
  writeGate = null;
  unassigned = false;
  calls = [];
});
describe('native technician components', () => {
  it('switches to the downloaded order on network loss and keeps a service note draft', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    let broadcast: ((state: Network.NetworkStateEvent) => void) | undefined;
    jest.mocked(Network.getNetworkStateAsync).mockResolvedValue({ isConnected: true });
    jest.mocked(Network.addNetworkStateListener).mockImplementation((listener) => {
      broadcast = listener;
      return { remove: jest.fn() };
    });
    const enqueue = jest.fn<OfflineStore['enqueue']>().mockResolvedValue(undefined);
    const work = { ...initialOrder, createdAt: '', updatedAt: '' };
    const snapshot: OfflineSnapshot = {
      cursor: 'cursor',
      lastSync: Date.now(),
      rebuilding: false,
      removed: 0,
      retryAt: 0,
      operations: [],
      orders: [
        {
          order: work as OfflineSnapshot['orders'][number]['order'],
          notes: [],
          checklist: {
            workOrderId: work.id,
            workOrderVersion: work.version,
            totalItems: 0,
            completedItems: 0,
            progressPercent: 0,
            requiredItemsComplete: true,
            items: [],
          },
        },
      ],
    };
    const vault = {
      snapshot: async () => snapshot,
      applyPage: async () => {},
      enqueue,
    };
    jest
      .mocked(openOfflineVault)
      .mockResolvedValue(vault as unknown as Awaited<ReturnType<typeof openOfflineVault>>);
    ctx.mount(
      <OfflineProvider>
        <WorkOrderRoute />
      </OfflineProvider>,
    );
    fireEvent.press(await screen.findByText('Notes'));
    fireEvent.changeText(await screen.findByLabelText('Service note'), 'Retain this draft');
    await waitFor(() => expect(calls.some(({ path }) => path.startsWith('/sync/pull'))).toBe(true));
    await act(async () => broadcast?.({ isConnected: false }));
    expect(await screen.findByText('Offline')).toBeOnTheScreen();
    expect(screen.getByDisplayValue('Retain this draft')).toBeOnTheScreen();
    fireEvent.press(screen.getByRole('button', { name: 'Save note' }));
    await waitFor(() =>
      expect(enqueue).toHaveBeenCalledWith(expect.any(String), 'order', 5, {
        kind: 'note',
        body: 'Retain this draft',
      }),
    );
    expect(
      calls.some(
        ({ path, body }) => (path.includes('/comments') && 'body' in body) || path === '/sync/push',
      ),
    ).toBe(false);
  });
  it('applies an accessible status filter through the API', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<OrdersScreen />);
    await screen.findByText('Inspect pump');
    fireEvent.press(screen.getByRole('button', { name: 'Status: All' }));
    fireEvent.press(screen.getByRole('radio', { name: 'In Progress' }));
    await waitFor(() =>
      expect(calls.some((call) => call.path.includes('status=InProgress'))).toBe(true),
    );
    expect(screen.queryByRole('radio')).toBeNull();
  });
  it('retains the login form and displays API rejection', async () => {
    const ctx = fixture();
    await ctx.session.bootstrap();
    ctx.mount(<SignIn />);
    rejectLogin = true;
    fireEvent.changeText(screen.getByLabelText('Email'), 'tech@test');
    fireEvent.changeText(screen.getByLabelText('Password'), 'secret');
    fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByText('Invalid credentials');
    expect(screen.getByLabelText('Email').props.value).toBe('tech@test');
    expect(ctx.session.snapshot().access).toBeNull();
  });
  it('opens a static encoded address in the native maps handler', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    fireEvent.press(await screen.findByRole('button', { name: 'Open in maps' }));
    expect(open).toHaveBeenCalledWith(
      'https://www.google.com/maps/search/?api=1&query=12%20Main%20Street%2C%20London%2C%20UK',
    );
    open.mockRestore();
  });
  it('disables aggregate controls while an API mutation is pending', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    const start = await screen.findByRole('button', { name: 'Start work' });
    await waitFor(() => expect(start).toBeEnabled());
    let release!: () => void;
    writeGate = new Promise((resolve) => {
      release = resolve;
    });
    fireEvent.press(start);
    fireEvent.press(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() => expect(screen.getByRole('checkbox')).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
    fireEvent.press(start);
    expect(calls.filter((call) => call.path.endsWith('/start'))).toHaveLength(1);
    await act(async () => {
      release();
    });
    await screen.findByRole('button', { name: 'Pause work' });
  });
  it('clears a note only after the server acknowledges its new aggregate version', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    await screen.findByText('Inspect pump');
    fireEvent.press(screen.getByRole('tab', { name: 'Notes' }));
    fireEvent.changeText(await screen.findByLabelText('Service note'), 'Pressure verified');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Add service note' })).toBeEnabled(),
    );
    fireEvent.press(screen.getByRole('button', { name: 'Add service note' }));
    await waitFor(() => expect(screen.getByLabelText('Service note').props.value).toBe(''));
    expect(calls.find((call) => call.path.endsWith('/comments'))?.body).toEqual({
      body: 'Pressure verified',
      expectedVersion: 5,
      visibility: 'TechnicianVisible',
    });
  });
  it('shows profile and clears cached work when sign-out is confirmed', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.client.setQueryData(['private'], { title: 'Sensitive work' });
    ctx.mount(<Profile />);
    await screen.findByText('tech@test');
    fireEvent.press(screen.getByRole('button', { name: 'Sign out' }));
    fireEvent.press(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() => expect(ctx.session.snapshot().status).toBe('signedOut'));
    expect(ctx.client.getQueryData(['private'])).toBeUndefined();
    await waitFor(() => expect(ctx.session.snapshot().busy).toBe(false));
  });
  it('submits real session login with protected password field', async () => {
    const ctx = fixture();
    await ctx.session.bootstrap();
    ctx.mount(<SignIn />);
    fireEvent.changeText(screen.getByLabelText('Email'), 'tech@test');
    fireEvent.changeText(screen.getByLabelText('Password'), 'secret');
    expect(screen.getByLabelText('Password').props.secureTextEntry).toBe(true);
    fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
    await waitFor(() => expect(ctx.session.snapshot().status).toBe('authenticated'));
    expect(calls.find((call) => call.path === '/auth/login')?.body).toEqual({
      email: 'tech@test',
      password: 'secret',
    });
  });
  it('loads assigned orders with current-user filter and server search', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<OrdersScreen />);
    await screen.findByText('Inspect pump');
    expect(calls.some((call) => call.path.includes('technicianUserId=tech'))).toBe(true);
    fireEvent.changeText(screen.getByLabelText('Search work orders'), 'Pump');
    fireEvent.press(screen.getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(calls.some((call) => call.path.includes('search=Pump'))).toBe(true));
  });
  it('starts with a frozen version and shares acknowledged versions with checklist writes', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    const start = await screen.findByRole('button', { name: 'Start work' });
    await waitFor(() => expect(start).toBeEnabled());
    fireEvent.press(start);
    fireEvent.press(screen.getByRole('button', { name: 'Confirm' }));
    await screen.findByRole('button', { name: 'Pause work' });
    expect(calls.find((call) => call.path.endsWith('/start'))?.body.expectedVersion).toBe(5);
    await waitFor(() => expect(screen.getByRole('checkbox')).toBeEnabled());
    fireEvent.press(screen.getByRole('checkbox'));
    await waitFor(() => expect(screen.getByRole('checkbox')).toBeChecked());
    expect(calls.find((call) => call.path.endsWith('/checklist/item'))?.body.expectedVersion).toBe(
      6,
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Complete work' })).toBeEnabled(),
    );
  });
  it('blocks completion before required checklist items are checked', async () => {
    order.status = 'InProgress';
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    expect(await screen.findByRole('button', { name: 'Complete work' })).toBeDisabled();
  });
  it('preserves service-note input on conflict and requires explicit reload', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    await screen.findByText('Inspect pump');
    fireEvent.press(screen.getByRole('tab', { name: 'Notes' }));
    const field = await screen.findByLabelText('Service note');
    fireEvent.changeText(field, 'Measured pressure');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Add service note' })).toBeEnabled(),
    );
    rejectWrite = true;
    fireEvent.press(screen.getByRole('button', { name: 'Add service note' }));
    await screen.findByText(/Changed by another user/);
    expect(field.props.value).toBe('Measured pressure');
    expect(screen.getByRole('button', { name: 'Add service note' })).toBeDisabled();
    rejectWrite = false;
    fireEvent.press(screen.getByRole('button', { name: 'Reload current data' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Add service note' })).toBeEnabled(),
    );
  });
  it('hides protected detail when the assignment is removed during refresh', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    ctx.mount(<WorkOrderScreen id="order" />);
    await screen.findByText('Inspect pump');
    unassigned = true;
    await act(async () => {
      await ctx.client.invalidateQueries();
    });
    expect(
      await screen.findByText('This work order is unavailable or no longer assigned to you.'),
    ).toBeTruthy();
    expect(screen.queryByText('Acme')).toBeNull();
  });
  it('serializes same-tick writes for the same aggregate', async () => {
    const client = {};
    let release!: () => void;
    const first = withOrderLock(
      client,
      'one',
      () =>
        new Promise<void>((resolve) => {
          release = resolve;
        }),
    );
    await expect(withOrderLock(client, 'one', async () => 'duplicate')).rejects.toMatchObject({
      code: 'write_pending',
    });
    expect(await withOrderLock(client, 'two', async () => 'other')).toBe('other');
    release();
    await first;
    expect(await withOrderLock(client, 'one', async () => 'next')).toBe('next');
  });
});

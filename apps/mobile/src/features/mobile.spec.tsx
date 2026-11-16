import { describe, expect, it, jest, beforeEach, afterEach } from '@jest/globals';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import {
  defaultScheduler,
  QueryClient,
  QueryClientProvider,
  notifyManager,
} from '@tanstack/react-query';
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
import { enablePush, PushLifecycle } from './notifications';
import { MobileAttachments } from './attachments';
import * as SecureStore from 'expo-secure-store';
import * as ImagePicker from 'expo-image-picker';
import * as Notifications from 'expo-notifications';
import { File } from 'expo-file-system';
import { shareAsync } from 'expo-sharing';
jest.mock('expo-image-picker', () => ({ requestCameraPermissionsAsync: jest.fn() }));
jest.mock('expo-file-system', () => ({
  Paths: { cache: { uri: 'file:///cache' } },
  File: jest.fn(),
  Directory: jest.fn(() => ({ exists: false })),
}));
jest.mock('expo-sharing', () => ({ isAvailableAsync: async () => true, shareAsync: jest.fn() }));
jest.mock('expo-notifications', () => ({
  setNotificationHandler: jest.fn(),
  getPermissionsAsync: jest.fn(),
  getExpoPushTokenAsync: jest.fn(),
  addPushTokenListener: jest.fn(),
  addNotificationResponseReceivedListener: () => ({ remove: jest.fn() }),
  addNotificationReceivedListener: () => ({ remove: jest.fn() }),
  getLastNotificationResponseAsync: async () => null,
}));
jest.mock('./push-native', () => ({
  loadPushNotifications: async () => jest.requireMock('expo-notifications'),
}));
jest.mock('expo-constants', () => ({ easConfig: { projectId: 'unit-test-project' } }));
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
function notifyWithinAct(callback: () => void) {
  act(callback);
}
notifyManager.setNotifyFunction(notifyWithinAct);
afterEach(() => {
  clients.forEach((client) => client.clear());
  clients.length = 0;
  notifyManager.setScheduler(defaultScheduler);
  notifyManager.setNotifyFunction(notifyWithinAct);
});
let order = { ...initialOrder };
let completed = false;
let rejectWrite = false;
let rejectLogin = false;
let writeGate: Promise<void> | null = null;
let unassigned = false;
let calls: { path: string; body: Record<string, unknown> }[];
const storedAttachment = {
  id: 'saved',
  fileName: 'service.png',
  contentType: 'image/png',
  size: 3,
};
function response(value: unknown, status = 200) {
  return {
    ok: status < 400,
    status,
    headers: new Headers(),
    json: async () => value,
  } as Response;
}
function fixture(
  overrides: {
    push?: (init?: RequestInit) => Promise<Response>;
    attachment?: (init?: RequestInit) => Promise<Response>;
    metadata?: typeof storedAttachment;
  } = {},
) {
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
    if (path === '/organizations/next/select')
      return response({
        ...token,
        tenantId: 'next',
        sessionId: 'next-session',
        accessToken: 'next-access',
      });
    if (path.startsWith('/notifications/devices/'))
      return overrides.push ? overrides.push(init) : response(null, 204);
    if (path.endsWith('/attachments'))
      return response(overrides.attachment ? [overrides.metadata ?? storedAttachment] : []);
    if (path.endsWith('/attachments/saved') && overrides.attachment)
      return overrides.attachment(init);
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
  it.each(['keep', 'logout', 'switch', 'unmount'] as const)(
    'handles a token rotation during an in-flight registration (%s)',
    async (change) => {
      const pending = Promise.withResolvers<Response>();
      const credentials: Array<{
        token: string;
        authorization: string | null;
        signal: AbortSignal | null | undefined;
      }> = [];
      const ctx = fixture({
        push: async (init) => {
          credentials.push({
            token: JSON.parse(String(init?.body)).token,
            authorization: new Headers(init?.headers).get('Authorization'),
            signal: init?.signal,
          });
          return credentials.length === 1 ? pending.promise : response(null, 204);
        },
      });
      await ctx.session.login('a', 'b');
      const stored = jest
        .spyOn(SecureStore, 'getItemAsync')
        .mockResolvedValue(
          JSON.stringify({ id: 'device-1', userId: 'tech', origin: 'https://api.test' }),
        );
      const remove = jest.fn();
      let emit!: Parameters<typeof Notifications.addPushTokenListener>[0];
      jest.mocked(Notifications.addPushTokenListener).mockImplementation((listener) => {
        emit = listener;
        return { remove };
      });
      jest
        .mocked(Notifications.getPermissionsAsync)
        .mockResolvedValue({ granted: true } as Awaited<
          ReturnType<typeof Notifications.getPermissionsAsync>
        >);
      jest.mocked(Notifications.getExpoPushTokenAsync).mockImplementation(async (options) => ({
        type: 'expo',
        data: `ExpoPushToken[${options?.devicePushToken?.data ?? 'initial'}]`,
      }));
      const view = ctx.mount(<PushLifecycle />);
      try {
        await waitFor(() => expect(credentials).toHaveLength(1));
        act(() => {
          emit({ type: 'ios', data: 'intermediate' });
          emit({ type: 'ios', data: 'latest' });
          emit({ type: 'ios', data: 'latest' });
        });
        if (change === 'logout')
          await act(async () => {
            await ctx.session.logout();
          });
        if (change === 'switch')
          await act(async () => {
            await ctx.session.selectOrganization('next');
          });
        if (change === 'unmount') view.unmount();
        await act(async () => {
          pending.resolve(response(null, 204));
        });
        const tokens = () => credentials.map((entry) => [entry.authorization, entry.token]);
        if (change === 'keep') {
          await waitFor(() =>
            expect(tokens()).toEqual([
              ['Bearer access', 'ExpoPushToken[initial]'],
              ['Bearer access', 'ExpoPushToken[latest]'],
            ]),
          );
          await act(async () => {
            emit({ type: 'ios', data: 'latest' });
          });
          expect(credentials).toHaveLength(2);
        } else {
          expect(credentials[0]?.signal?.aborted).toBe(true);
          expect(remove).toHaveBeenCalled();
          if (change === 'switch')
            await waitFor(() =>
              expect(tokens()).toEqual([
                ['Bearer access', 'ExpoPushToken[initial]'],
                ['Bearer next-access', 'ExpoPushToken[initial]'],
              ]),
            );
          else expect(credentials).toHaveLength(1);
        }
      } finally {
        view.unmount();
        stored.mockRestore();
      }
    },
  );

  it.each(['preview', 'pdf', 'denied', 'truncated', 'logout-pending'] as const)(
    'opens a stored attachment through the authenticated API and cleans up (%s)',
    async (scenario) => {
      const create = jest.fn();
      const remove = jest.fn();
      const writeBytes = jest.fn<(_: Uint8Array) => Promise<void>>().mockResolvedValue(undefined);
      const close = jest.fn();
      const pdf = scenario === 'pdf';
      const metadata = pdf
        ? { ...storedAttachment, fileName: 'service.pdf', contentType: 'application/pdf' }
        : storedAttachment;
      const uri = `file:///cache/private-preview.${pdf ? 'pdf' : 'png'}`;
      jest.mocked(File).mockImplementation(
        () =>
          ({
            uri,
            get exists() {
              return create.mock.calls.length > remove.mock.calls.length;
            },
            create,
            delete: remove,
            open: () => ({ writeBytes, close }),
          }) as unknown as jest.Mocked<File>,
      );
      const bytes = Uint8Array.of(1, 2, 3).slice(0, scenario === 'truncated' ? 2 : 3);
      const download = Promise.withResolvers<void>();
      const sent = Promise.withResolvers<AbortSignal | null | undefined>();
      const attachment = jest.fn<(init?: RequestInit) => Promise<Response>>(async (init) => {
        expect(new Headers(init?.headers).get('Authorization')).toEqual('Bearer access');
        expect(init?.redirect).toBe('error');
        if (scenario === 'denied')
          return response({ detail: 'The attachment was not found.' }, 404);
        sent.resolve(init?.signal);
        const read = jest
          .fn<() => Promise<{ done: boolean; value?: Uint8Array }>>()
          .mockResolvedValueOnce({ done: false, value: bytes })
          .mockResolvedValue({ done: true });
        return {
          ...response(null),
          headers: new Headers({ 'content-type': metadata.contentType }),
          body: {
            getReader: () => ({
              read: async () => {
                if (scenario === 'logout-pending') await download.promise;
                return read();
              },
              releaseLock: jest.fn(),
              cancel: async () => undefined,
            }),
          },
        } as unknown as Response;
      });
      const ctx = fixture({ attachment, metadata });
      await ctx.session.login('a', 'b');
      ctx.mount(<MobileAttachments id="order" />);
      fireEvent.press(await screen.findByRole('button', { name: `View ${metadata.fileName}` }));
      if (scenario === 'denied') {
        await screen.findByText(/attachment was not found/i);
        expect(create).not.toHaveBeenCalled();
      } else if (scenario === 'truncated') {
        await screen.findByText(/download was incomplete/i);
        expect(remove).toHaveBeenCalledTimes(1);
      } else if (scenario === 'pdf') {
        fireEvent.press(await screen.findByRole('button', { name: 'Open PDF in another app' }));
        await waitFor(() =>
          expect(shareAsync).toHaveBeenCalledWith(uri, {
            mimeType: 'application/pdf',
            UTI: 'com.adobe.pdf',
          }),
        );
        fireEvent.press(screen.getByRole('button', { name: 'Close attachment' }));
        expect(remove).toHaveBeenCalledTimes(1);
      } else if (scenario === 'logout-pending') {
        const signal = await sent.promise;
        await waitFor(() => expect(create).toHaveBeenCalled());
        await act(async () => {
          await ctx.session.logout();
        });
        expect(signal?.aborted).toBe(true);
        await act(async () => {
          download.resolve();
        });
        await waitFor(() => expect(remove).toHaveBeenCalledTimes(1));
        expect(writeBytes).not.toHaveBeenCalled();
        expect(screen.queryByLabelText('Preview of service.png')).toBeNull();
      } else {
        expect((await screen.findByLabelText('Preview of service.png')).props.source).toEqual({
          uri,
        });
        expect(writeBytes).toHaveBeenCalledWith(bytes);
        expect(close).toHaveBeenCalledTimes(1);
        await act(async () => {
          await ctx.session.logout();
        });
        expect(screen.queryByLabelText('Preview of service.png')).toBeNull();
        expect(remove).toHaveBeenCalledTimes(1);
      }
      expect(attachment).toHaveBeenCalledTimes(1);
    },
  );

  it('explains camera denial without preventing an existing attachment selection', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    jest.mocked(ImagePicker.requestCameraPermissionsAsync).mockResolvedValueOnce({
      granted: false,
      canAskAgain: true,
      expires: 'never',
      status: 'denied',
    } as Awaited<ReturnType<typeof ImagePicker.requestCameraPermissionsAsync>>);
    ctx.mount(<WorkOrderScreen id="order" />);
    fireEvent.press(await screen.findByRole('button', { name: 'Take photo' }));
    expect(await screen.findByText(/Camera permission is required/)).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Choose document' })).toBeEnabled();
  });
  it('requests push on opt-in, registers it for this session, and revokes it on denied permission', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    const stored = jest
      .spyOn(SecureStore, 'getItemAsync')
      .mockResolvedValue(
        JSON.stringify({ id: 'device-1', userId: 'tech', origin: 'https://api.test' }),
      );
    const denied = { granted: false, canAskAgain: true };
    const getPermissionsAsync = jest.fn<() => Promise<typeof denied>>().mockResolvedValue(denied);
    const requestPermissionsAsync = jest
      .fn<() => Promise<typeof denied>>()
      .mockResolvedValue({ granted: true, canAskAgain: true });
    const getExpoPushTokenAsync = jest
      .fn<() => Promise<{ data: string }>>()
      .mockResolvedValue({ data: 'ExpoPushToken[test-token]' });
    const adapter = {
      getPermissionsAsync,
      requestPermissionsAsync,
      getExpoPushTokenAsync,
    } as unknown as Parameters<typeof enablePush>[1];
    try {
      expect(await enablePush(ctx.session, adapter, true)).toBe(true);
      expect(requestPermissionsAsync).toHaveBeenCalledTimes(1);
      expect(calls).toContainEqual({
        path: '/notifications/devices/device-1',
        body: { token: 'ExpoPushToken[test-token]' },
      });
      expect(await enablePush(ctx.session, adapter, false)).toBe(false);
      expect(calls.at(-1)).toEqual({ path: '/notifications/devices/device-1', body: {} });
      expect(getExpoPushTokenAsync).toHaveBeenCalledTimes(1);
    } finally {
      stored.mockRestore();
    }
  });
  it('switches to the downloaded order on network loss and keeps a service note draft', async () => {
    const ctx = fixture();
    await ctx.session.login('a', 'b');
    const initialNetwork = Promise.withResolvers<Network.NetworkState>();
    jest.mocked(Network.getNetworkStateAsync).mockReturnValueOnce(initialNetwork.promise);
    jest.mocked(Network.addNetworkStateListener).mockReturnValueOnce({ remove: jest.fn() });
    const enqueue = jest.fn<OfflineStore['enqueue']>().mockResolvedValue(undefined);
    const pageApplied = Promise.withResolvers<void>();
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
      applyPage: async () => pageApplied.resolve(),
      enqueue,
    };
    jest
      .mocked(openOfflineVault)
      .mockResolvedValueOnce(vault as unknown as Awaited<ReturnType<typeof openOfflineVault>>);
    // This test drives the mocked I/O inside act, including the Query notifications.
    notifyManager.setScheduler(queueMicrotask);
    notifyManager.setNotifyFunction((callback) => callback());
    ctx.mount(
      <OfflineProvider>
        <WorkOrderRoute />
      </OfflineProvider>,
    );
    await act(async () => {
      initialNetwork.resolve({ isConnected: true });
      await pageApplied.promise;
    });
    await waitFor(() => {
      expect(screen.getByRole('header', { name: work.title })).toBeOnTheScreen();
      expect(screen.getByRole('button', { name: 'Start work' })).toBeEnabled();
    });
    await act(async () =>
      fireEvent.press(screen.getByRole('tab', { name: 'Notes', selected: false })),
    );
    expect(screen.getByRole('tab', { name: 'Notes', selected: true })).toBeOnTheScreen();
    fireEvent.changeText(screen.getByLabelText('Service note'), 'Retain this draft');
    expect(screen.getByDisplayValue('Retain this draft')).toBeOnTheScreen();
    const [networkChanged] = jest.mocked(Network.addNetworkStateListener).mock.lastCall ?? [];
    if (!networkChanged)
      throw new Error('The offline workspace did not subscribe to connectivity.');
    await act(async () => networkChanged({ isConnected: false }));
    expect(await screen.findByText('Offline')).toBeOnTheScreen();
    expect(screen.getByText(work.title)).toBeOnTheScreen();
    expect(screen.getByText(`Version ${work.version}`)).toBeOnTheScreen();
    expect(screen.getByDisplayValue('Retain this draft')).toBeOnTheScreen();
    const save = screen.getByRole('button', { name: 'Save note' });
    expect(save).toBeEnabled();
    await act(async () => fireEvent.press(save));
    expect(enqueue).toHaveBeenCalledTimes(1);
    expect(enqueue).toHaveBeenCalledWith(expect.any(String), work.id, work.version, {
      kind: 'note',
      body: 'Retain this draft',
    });
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

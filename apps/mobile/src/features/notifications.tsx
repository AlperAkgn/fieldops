import { useEffect, useRef, useState } from 'react';
import { AppState, Platform, Text } from 'react-native';
import { router } from 'expo-router';
import Constants from 'expo-constants';
import * as SecureStore from 'expo-secure-store';
import { randomUUID } from 'expo-crypto';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSession } from '../auth/runtime';
import { sameOfflineScope, type MobileSession } from '../auth/session';
import type { Page } from '../api/contracts';
import { useOffline } from '../offline/runtime';
import { Button, Card, ErrorBox, Loading, Screen, styles } from '../components/ui';
import { useApi } from './queries';
import { message } from './orders';
import { loadPushNotifications } from './push-native';

type Notification = {
  id: string;
  kind: string;
  workOrderId: string | null;
  createdAt: string;
  readAt: string | null;
};
const deviceKey = 'fieldops.push.installation.v1';
const settings = {
  keychainService: 'com.fieldops.notifications',
  keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
};
type NotificationApi = typeof import('expo-notifications');
type Installation = { id: string; userId: string; origin: string };
type PushTokenOptions = Parameters<NotificationApi['getExpoPushTokenAsync']>[0];
const registrations = new WeakMap<MobileSession, Promise<boolean>>();

export async function enablePush(
  session: MobileSession,
  notifications: NotificationApi,
  prompt: boolean,
  deviceToken?: PushTokenOptions,
  signal?: AbortSignal,
) {
  const owner = session.snapshot().access;
  if (
    Platform.OS === 'web' ||
    owner?.role !== 'Technician' ||
    !session.transport.origin.startsWith('https:')
  )
    return false;
  const active = () => !signal?.aborted && sameOfflineScope(session.snapshot().access, owner);
  const result = (registrations.get(session)?.catch(() => false) ?? Promise.resolve()).then(
    register,
  );
  registrations.set(session, result);
  try {
    return await result;
  } finally {
    if (registrations.get(session) === result) registrations.delete(session);
  }

  async function register() {
    if (!owner || !active()) return false;
    const options = { expectedScope: owner, ...(signal ? { signal } : {}) };
    if (Platform.OS === 'android')
      await notifications.setNotificationChannelAsync('work-updates', {
        name: 'Work updates',
        importance: notifications.AndroidImportance.DEFAULT,
      });
    let permission = await notifications.getPermissionsAsync();
    if (prompt && !permission.granted && permission.canAskAgain && active())
      permission = await notifications.requestPermissionsAsync();
    const record = await SecureStore.getItemAsync(deviceKey, settings);
    let installation: Installation | null = null;
    try {
      installation = record ? (JSON.parse(record) as Installation) : null;
    } catch {
      /* Register a new installation after opt-in. */
    }
    const optedIn =
      installation?.userId === owner.userId && installation.origin === session.transport.origin;
    const deviceId = installation?.id;
    if (!permission.granted) {
      if (deviceId && optedIn && active())
        await session.request(`/notifications/devices/${deviceId}`, {
          method: 'DELETE',
          ...options,
        });
      return false;
    }
    if (!active() || (!prompt && !optedIn)) return false;
    const projectId = Constants.easConfig?.projectId ?? Constants.expoConfig?.extra?.eas?.projectId;
    if (typeof projectId !== 'string')
      throw new Error('This build has no Expo push project configured.');
    const token = await notifications.getExpoPushTokenAsync({ projectId, ...deviceToken });
    if (!active()) return false;
    const id = deviceId ?? randomUUID();
    if (!optedIn)
      await SecureStore.setItemAsync(
        deviceKey,
        JSON.stringify({
          id,
          userId: owner.userId,
          origin: session.transport.origin,
        } satisfies Installation),
        settings,
      );
    if (!active()) return false;
    await session.request(`/notifications/devices/${id}`, {
      method: 'PUT',
      body: { token: token.data },
      ...options,
    });
    return active();
  }
}

export function PushLifecycle() {
  const { session, state } = useSession();
  const { offline } = useOffline();
  const client = useQueryClient();
  const id = state.access?.role === 'Technician' ? state.access.sessionId : null;
  const tenant = state.access?.tenantId;
  const user = state.access?.userId;
  useEffect(() => {
    if (!id || offline || Platform.OS === 'web') return;
    const lifetime = new AbortController();
    let cleanup: (() => void) | undefined;
    const active = () => {
      const current = session.snapshot().access;
      return (
        !lifetime.signal.aborted &&
        current?.sessionId === id &&
        current.tenantId === tenant &&
        current.userId === user &&
        current.role === 'Technician'
      );
    };
    const received = () => {
      if (active())
        void client.invalidateQueries({
          predicate: ({ queryKey }) => queryKey[4] === id && queryKey[5] === 'notifications',
        });
    };
    void loadPushNotifications()
      .then((notifications) => {
        if (!active()) return;
        notifications.setNotificationHandler({
          handleNotification: async () => ({
            shouldShowBanner: true,
            shouldShowList: true,
            shouldPlaySound: false,
            shouldSetBadge: false,
          }),
        });
        const tap = (
          response: NotificationApi extends {
            getLastNotificationResponseAsync: () => Promise<infer Response>;
          }
            ? Response
            : never,
        ) => {
          if (
            !active() ||
            !response ||
            typeof response.notification.request.content.data?.notificationId !== 'string'
          )
            return;
          void notifications.clearLastNotificationResponseAsync().catch(() => undefined);
          received();
          // The authenticated inbox checks access before showing a work-order link.
          router.push('/notifications');
        };
        let running = false;
        let pending: { options: PushTokenOptions } | undefined;
        let registeredToken: string | undefined;
        const register = (options?: PushTokenOptions) => {
          if (!active() || (!options && running)) return;
          pending = { options };
          if (running) return;
          running = true;
          void (async () => {
            while (active() && pending) {
              const { options: next } = pending;
              pending = undefined;
              const token = next?.devicePushToken?.data;
              if (typeof token === 'string' && token === registeredToken) continue;
              if (
                await enablePush(session, notifications, false, next, lifetime.signal).catch(
                  () => false,
                )
              )
                registeredToken = typeof token === 'string' ? token : undefined;
            }
            running = false;
          })();
        };
        const token = notifications.addPushTokenListener((next) =>
          register({ devicePushToken: next }),
        );
        const response = notifications.addNotificationResponseReceivedListener(tap);
        const foreground = notifications.addNotificationReceivedListener(received);
        const app = AppState.addEventListener('change', (next) => {
          if (next === 'active') register();
        });
        cleanup = () => {
          token.remove();
          response.remove();
          foreground.remove();
          app.remove();
          notifications.setNotificationHandler(null);
        };
        void notifications
          .getLastNotificationResponseAsync()
          .then(tap)
          .catch(() => undefined);
        register();
      })
      .catch(() => undefined);
    return () => {
      lifetime.abort();
      cleanup?.();
    };
  }, [client, id, offline, session, tenant, user]);
  return null;
}

export function MobileNotifications() {
  const { session, state, scope } = useApi();
  const { offline } = useOffline();
  const client = useQueryClient();
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);
  const [page, setPage] = useState(1);
  const permissionRequest = useRef<AbortController | null>(null);
  const sessionId = state.access?.sessionId;
  useEffect(() => () => permissionRequest.current?.abort(), [sessionId, offline]);
  const key = [...scope, 'notifications'];
  const query = useQuery({
    queryKey: [...key, page],
    refetchInterval: 30_000,
    enabled: !offline && state.status === 'authenticated',
    queryFn: ({ signal }) =>
      session.request<Page<Notification>>(`/notifications?page=${page}&pageSize=20`, { signal }),
  });
  async function permission() {
    const controller = new AbortController();
    permissionRequest.current = controller;
    setBusy(true);
    setError('');
    setStatus('');
    try {
      const enabled = await enablePush(
        session,
        await loadPushNotifications(),
        true,
        undefined,
        controller.signal,
      );
      if (controller.signal.aborted) return;
      setStatus(
        enabled
          ? 'Push notifications enabled.'
          : 'Push is unavailable or permission is disabled. Workspace notifications are still available here.',
      );
    } catch (failure) {
      if (!controller.signal.aborted) setError(message(failure));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Screen>
      <Button title="Back to profile" secondary onPress={() => router.replace('/(tabs)/profile')} />
      <Text style={styles.title}>Notifications</Text>
      {offline ? (
        <Text style={styles.text}>Connect to view workspace notifications.</Text>
      ) : (
        <>
          {state.access?.role === 'Technician' && (
            <Button
              title="Enable push notifications"
              disabled={busy}
              onPress={() => void permission()}
            />
          )}
          {status ? (
            <Text accessibilityLiveRegion="polite" style={styles.text}>
              {status}
            </Text>
          ) : null}
          {error ? <ErrorBox text={error} /> : null}
          {query.isLoading && <Loading />}
          {query.error && (
            <ErrorBox text={message(query.error)} retry={() => void query.refetch()} />
          )}
          {query.data?.items.length === 0 && (
            <Text style={styles.text}>No workspace notifications.</Text>
          )}
          {query.data?.items.map((row) => (
            <Card key={row.id}>
              <Text style={styles.text}>{row.kind.replaceAll('_', ' ')}</Text>
              <Text style={styles.subtitle}>{new Date(row.createdAt).toLocaleString()}</Text>
              {row.workOrderId && (
                <Button
                  title="Open work order"
                  onPress={() => {
                    if (row.workOrderId)
                      router.push({
                        pathname: '/work-orders/[id]',
                        params: { id: row.workOrderId },
                      });
                  }}
                />
              )}
              {!row.readAt && (
                <Button
                  title="Mark read"
                  secondary
                  onPress={() => {
                    void session
                      .request(`/notifications/${row.id}/read`, { method: 'POST' })
                      .then(() => client.invalidateQueries({ queryKey: key }))
                      .catch((failure) => setError(message(failure)));
                  }}
                />
              )}
            </Card>
          ))}
          {page > 1 && (
            <Button title="Previous notifications" secondary onPress={() => setPage(page - 1)} />
          )}
          {!!query.data && page * 20 < query.data.totalCount && (
            <Button title="Next notifications" secondary onPress={() => setPage(page + 1)} />
          )}
        </>
      )}
    </Screen>
  );
}

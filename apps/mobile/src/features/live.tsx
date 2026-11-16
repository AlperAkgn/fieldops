import { useEffect } from 'react';
import { AppState, Platform } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { useSession } from '../auth/runtime';
import { useOffline } from '../offline/runtime';

export function LiveUpdates() {
  const { session, state } = useSession();
  const { workspace, offline } = useOffline();
  const client = useQueryClient();
  const id = state.access?.role === 'Technician' ? state.access.sessionId : null;
  useEffect(() => {
    if (
      !id ||
      !workspace ||
      offline ||
      Platform.OS === 'web' ||
      !session.transport.origin.startsWith('https:')
    )
      return;
    let disposed = false;
    let stop: (() => Promise<void>) | undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let reload: ReturnType<typeof setTimeout> | undefined;
    const active = () =>
      !disposed &&
      session.snapshot().access?.sessionId === id &&
      session.snapshot().access?.role === 'Technician';
    const reconcile = () => {
      if (!active() || reload) return;
      reload = setTimeout(() => {
        reload = undefined;
        if (!active()) return;
        void workspace.sync();
        void client.invalidateQueries({
          predicate: ({ queryKey }) =>
            queryKey[0] === 'mobile' &&
            queryKey[4] === id &&
            ['notifications', 'attachments'].includes(String(queryKey[5])),
        });
      }, 120);
    };
    void import('@microsoft/signalr')
      .then(({ HubConnectionBuilder, HubConnectionState, HttpTransportType, LogLevel }) => {
        if (!active()) return;
        const connection = new HubConnectionBuilder()
          .configureLogging(LogLevel.None)
          .withUrl(`${session.transport.origin}/api/v1/live`, {
            transport: HttpTransportType.WebSockets,
            withCredentials: false,
            accessTokenFactory: async () => {
              const access = session.snapshot().access;
              if (!access || !active()) throw new Error('Workspace changed.');
              // Phase 5 owns credential rotation and reachability checks.
              if (Date.parse(access.expiresAt) <= Date.now()) await workspace.sync();
              const latest = session.snapshot().access;
              if (!latest || !active()) throw new Error('Workspace changed.');
              return latest.accessToken;
            },
          })
          .withAutomaticReconnect()
          .build();
        const seen = new Set<string>();
        const start = () => {
          if (
            !active() ||
            AppState.currentState === 'background' ||
            connection.state !== HubConnectionState.Disconnected
          )
            return;
          clearTimeout(timer);
          void connection.start().then(reconcile, retry);
        };
        const retry = () => {
          clearTimeout(timer);
          if (active() && AppState.currentState !== 'background') timer = setTimeout(start, 5000);
        };
        connection.on('OrderChanged', (event: { eventId?: string }) => {
          if (!event?.eventId || seen.has(event.eventId) || !active()) return;
          seen.add(event.eventId);
          if (seen.size > 256) seen.delete(seen.values().next().value!);
          reconcile();
        });
        connection.onclose(retry);
        connection.onreconnected(reconcile);
        const subscription = AppState.addEventListener('change', (value) => {
          if (value === 'active') start();
          else if (value === 'background') {
            clearTimeout(timer);
            void connection
              .stop()
              .catch(() => undefined)
              .finally(() => {
                if (AppState.currentState === 'active') start();
              });
          }
        });
        stop = () => {
          subscription.remove();
          return connection.stop();
        };
        start();
      })
      .catch(() => undefined);
    return () => {
      disposed = true;
      clearTimeout(timer);
      clearTimeout(reload);
      void stop?.().catch(() => undefined);
    };
  }, [client, session, id, workspace, offline]);
  return null;
}

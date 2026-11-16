import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { apiOrigin, refresh, sessionStore } from './client';
import { useSession } from './session';

const liveResources = new Set([
  'work-orders',
  'comments',
  'checklist',
  'history',
  'audit',
  'report',
  'reports',
  'audit-logs',
  'notifications',
]);

export function useRealtime() {
  const client = useQueryClient();
  const session = useSession();
  const scope = session?.sessionId;
  const role = session?.role;
  const tenant = session?.tenantId;
  const user = session?.userId;
  useEffect(() => {
    if (!scope || !role || !tenant || !user) return;
    const isActive = () => {
      const current = sessionStore.getSnapshot();
      return (
        !stopped &&
        current?.sessionId === scope &&
        current.role === role &&
        current.tenantId === tenant &&
        current.userId === user
      );
    };
    let stopped = false;
    let flush: ReturnType<typeof setTimeout> | undefined;
    let reconnect: ReturnType<typeof setTimeout> | undefined;
    const seen = new Set<string>();
    const invalidate = () => {
      if (!isActive() || flush) return;
      flush = setTimeout(() => {
        flush = undefined;
        if (!isActive()) return;
        void client.invalidateQueries({
          predicate: ({ queryKey }) =>
            queryKey[0] === tenant &&
            queryKey[1] === user &&
            queryKey[2] === role &&
            queryKey[3] === scope &&
            typeof queryKey[4] === 'string' &&
            liveResources.has(queryKey[4]),
        });
      }, 120);
    };
    let stop: (() => Promise<void>) | undefined;
    void import('@microsoft/signalr')
      .then(({ HubConnectionBuilder, HttpTransportType, LogLevel }) => {
        if (!isActive()) return;
        const connection = new HubConnectionBuilder()
          .configureLogging(LogLevel.None)
          .withUrl(`${apiOrigin()}/api/v1/live`, {
            transport: HttpTransportType.WebSockets,
            withCredentials: false,
            accessTokenFactory: async () => {
              const current = sessionStore.getSnapshot();
              if (!current || !isActive()) throw new Error('Workspace changed.');
              const token =
                Date.parse(current.expiresAt) <= Date.now() + 30_000 ? await refresh() : current;
              if (!isActive() || token.sessionId !== scope) throw new Error('Workspace changed.');
              return token.accessToken;
            },
          })
          .withAutomaticReconnect()
          .build();
        const start = () => {
          if (!isActive()) return;
          clearTimeout(reconnect);
          void connection.start().then(invalidate, () => {
            if (isActive()) reconnect = setTimeout(start, 5000);
          });
        };
        connection.on('OrderChanged', (event: { eventId?: string }) => {
          if (!event?.eventId || seen.has(event.eventId) || !isActive()) return;
          seen.add(event.eventId);
          if (seen.size > 256) seen.delete(seen.values().next().value!);
          invalidate();
        });
        connection.onreconnected(invalidate);
        connection.onclose(() => {
          clearTimeout(reconnect);
          if (isActive()) reconnect = setTimeout(start, 5000);
        });
        stop = () => connection.stop();
        start();
      })
      .catch(() => undefined);
    return () => {
      stopped = true;
      clearTimeout(flush);
      clearTimeout(reconnect);
      void stop?.().catch(() => undefined);
    };
  }, [client, scope, role, tenant, user]);
}

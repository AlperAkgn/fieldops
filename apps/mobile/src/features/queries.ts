import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSession } from '../auth/runtime';
import { ApiError } from '../api/transport';
import { TechnicianApi } from './orders';
import type { Checklist, OrderDetail, Organization } from '../api/contracts';

export function useApi() {
  const { session, state } = useSession();
  const api = useMemo(() => new TechnicianApi(session), [session]);
  const scope = [
    'mobile',
    state.access?.tenantId,
    state.access?.userId,
    state.access?.role,
    state.access?.sessionId,
  ];
  return { api, session, state, scope };
}
export function useWorkspace() {
  const { session, state, scope } = useApi();
  return useQuery({
    queryKey: [...scope, 'organizations'],
    enabled: state.status === 'authenticated' && !state.busy,
    queryFn: async ({ signal }) => {
      const rows = await session.request<Organization[]>('/organizations', {
        signal,
      });
      return {
        rows,
        current: rows.find((row) => row.id === state.access?.tenantId),
      };
    },
  });
}
export function useOrder(id: string) {
  const { api, state, scope } = useApi();
  const key = [...scope, 'order', id];
  const enabled = state.access?.role === 'Technician' && !state.busy;
  const detail = useQuery({
    queryKey: [...key, 'detail'],
    enabled,
    queryFn: ({ signal }) => api.detail(id, signal),
  });
  const checklist = useQuery({
    queryKey: [...key, 'checklist'],
    enabled,
    queryFn: ({ signal }) => api.checklist(id, signal),
  });
  return { detail, checklist, key, api, sessionBusy: state.busy };
}
const locks = new WeakMap<object, Set<string>>();
// One lock across every writer for this aggregate, including same-tick presses.
export async function withOrderLock<T>(client: object, key: string, operation: () => Promise<T>) {
  let set = locks.get(client);
  if (!set) {
    set = new Set();
    locks.set(client, set);
  }
  if (set.has(key)) throw new ApiError(409, 'write_pending', 'Wait for the current change.');
  set.add(key);
  try {
    return await operation();
  } finally {
    set.delete(key);
  }
}
export function useOrderWrite(id: string) {
  const { scope } = useApi();
  const client = useQueryClient();
  const key = [...scope, 'order', id];
  return useMutation({
    mutationKey: key,
    mutationFn: (
      operation: () => Promise<Checklist | OrderDetail['order'] | { workOrderVersion: number }>,
    ) =>
      withOrderLock(client, JSON.stringify(key), async () => {
        await client.cancelQueries({ queryKey: key });
        try {
          const result = await operation();
          const version = 'version' in result ? result.version : result.workOrderVersion;
          client.setQueryData<OrderDetail>([...key, 'detail'], (old) =>
            old
              ? {
                  ...old,
                  order: {
                    ...old.order,
                    ...('version' in result ? result : {}),
                    version,
                  },
                }
              : old,
          );
          if ('items' in result) client.setQueryData([...key, 'checklist'], result);
          else
            client.setQueryData<Checklist>([...key, 'checklist'], (old) =>
              old ? { ...old, workOrderVersion: version } : old,
            );
          await client.invalidateQueries({ queryKey: scope });
          return result;
        } catch (error) {
          if (error instanceof ApiError && (error.status === 403 || error.status === 404)) {
            client.removeQueries({ queryKey: key });
            client.removeQueries({ queryKey: [...scope, 'orders'] });
          }
          throw error;
        }
      }),
    retry: false,
  });
}

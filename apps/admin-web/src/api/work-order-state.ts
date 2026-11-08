import {
  useIsFetching,
  useIsMutating,
  useMutation,
  useQueryClient,
  type QueryClient,
} from '@tanstack/react-query';
import { api, ApiError } from './client';
import type { Checklist, OrderDetail, WorkOrder } from './contracts';
import { useSession } from './session';
import { useToast } from '../components/ui-state';

const locks = new WeakMap<QueryClient, Set<string>>();
export function useWorkOrderState(id: string) {
  const session = useSession();
  const scope = [session?.tenantId, session?.userId, session?.role, session?.sessionId];
  const detailKey = [...scope, 'work-orders', id];
  const mutationKey = [...scope, 'work-order-write', id];
  const writing = useIsMutating({ mutationKey, exact: true });
  const fetching = useIsFetching({ queryKey: detailKey, exact: true });
  return { scope, detailKey, mutationKey, busy: writing > 0 || fetching > 0 };
}

export function useWorkOrderAction(id: string, onSuccess?: () => void) {
  const client = useQueryClient();
  const state = useWorkOrderState(id);
  const toast = useToast();
  const lockId = JSON.stringify(state.mutationKey);
  const related = (query: { queryKey: readonly unknown[] }) =>
    state.scope.every((value, index) => query.queryKey[index] === value) &&
    (query.queryKey[4] === 'work-orders' ||
      query.queryKey[4] === 'report' ||
      (['checklist', 'comments', 'history', 'audit'].includes(String(query.queryKey[4])) &&
        query.queryKey[5] === id));
  const reload = () => client.invalidateQueries({ predicate: related }, { throwOnError: true });
  const mutation = useMutation({
    mutationKey: state.mutationKey,
    onMutate: async () => {
      const active = locks.get(client) ?? new Set<string>();
      locks.set(client, active);
      if (active.has(lockId))
        throw new ApiError(
          409,
          'update_in_progress',
          'Wait for the current work order update to finish.',
        );
      active.add(lockId);
      try {
        await client.cancelQueries({ predicate: related });
        return { ownsLock: true };
      } catch (error) {
        active.delete(lockId);
        throw error;
      }
    },
    mutationFn: ({
      path,
      method = 'POST',
      body,
    }: {
      path: string;
      method?: string;
      body: Record<string, unknown>;
    }) => {
      const current = client.getQueryData<OrderDetail>(state.detailKey);
      // Preserve the user's displayed/edited snapshot. Never upgrade a stale command
      // to somebody else's version or automatically replay a rejected write.
      if (current && body.expectedVersion !== current.order.version)
        throw new ApiError(
          409,
          'work_order_version_conflict',
          'The work order changed. Reload current data before retrying.',
        );
      return api<WorkOrder | Checklist | { workOrderVersion: number }>(path, { method, body });
    },
    onSuccess: async (result) => {
      const version = 'workOrderVersion' in result ? result.workOrderVersion : result.version;
      client.setQueryData<OrderDetail>(state.detailKey, (current) => {
        if (!current || current.order.version > version) return current;
        return {
          ...current,
          order: 'workOrderVersion' in result ? { ...current.order, version } : result,
        };
      });
      if ('items' in result) client.setQueryData([...state.scope, 'checklist', id], result);
      await reload(); // Remain pending through authoritative refetch, including failures.
      toast('Changes saved.');
      onSuccess?.();
    },
    onSettled: (_data, _error, _variables, context) => {
      if (context?.ownsLock) locks.get(client)?.delete(lockId);
    },
  });
  return { ...mutation, busy: state.busy, reload };
}

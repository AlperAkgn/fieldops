import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { ApiError } from './client';
import { useWorkOrderAction, useWorkOrderState } from './work-order-state';
const mocks = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('./client', async (original) => ({
  ...(await original()),
  api: (...args: unknown[]) => mocks.api(...args),
}));
vi.mock('./session', () => ({
  useSession: () => ({ tenantId: 'tenant', userId: 'user', role: 'Manager', sessionId: 'session' }),
}));
afterEach(() => vi.clearAllMocks());
const key = ['tenant', 'user', 'Manager', 'session', 'work-orders', 'order'];
function fixture(fetchVersion?: () => Promise<number>) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 10000 }, mutations: { retry: false } },
  });
  client.setQueryData(key, { order: { id: 'order', version: 4 } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const hook = renderHook(
    () => {
      useQuery({
        queryKey: key,
        queryFn: async () => ({ order: { id: 'order', version: await fetchVersion!() } }),
        enabled: !!fetchVersion,
      });
      return {
        a: useWorkOrderAction('order'),
        b: useWorkOrderAction('order'),
        state: useWorkOrderState('order'),
      };
    },
    { wrapper },
  );
  const version = () => client.getQueryData<{ order: { version: number } }>(key)!.order.version;
  return { ...hook, client, version };
}
const command = (version: number) => ({
  path: '/work-orders/order/checklist/step',
  method: 'PATCH',
  body: { expectedVersion: version, isCompleted: true },
});
describe('work order write coordination', () => {
  it('uses acknowledged versions for consecutive checklist writes', async () => {
    mocks.api
      .mockResolvedValueOnce({ workOrderVersion: 5 })
      .mockResolvedValueOnce({ workOrderVersion: 6 });
    const f = fixture();
    await act(async () => {
      await f.result.current.a.mutateAsync(command(f.version()));
    });
    await act(async () => {
      await f.result.current.b.mutateAsync(command(f.version()));
    });
    expect(mocks.api.mock.calls.map(([, options]) => options.body.expectedVersion)).toEqual([4, 5]);
    expect(f.version()).toBe(6);
  });
  it('prevents rapid writes through different components before React rerenders', async () => {
    let finish: (data: { workOrderVersion: number }) => void = () => undefined;
    mocks.api.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const f = fixture();
    let first: Promise<unknown>;
    await act(async () => {
      first = f.result.current.a.mutateAsync(command(4));
    });
    await waitFor(() => expect(mocks.api).toHaveBeenCalledTimes(1));
    await act(async () => {
      await expect(f.result.current.b.mutateAsync(command(4))).rejects.toMatchObject({
        code: 'update_in_progress',
      });
    });
    expect(f.result.current.state.busy).toBe(true);
    await act(async () => {
      finish({ workOrderVersion: 5 });
      await first!;
    });
    expect(f.version()).toBe(5);
    expect(mocks.api).toHaveBeenCalledTimes(1);
  });
  it('keeps real conflicts visible, reloads explicitly and never auto-replays a write', async () => {
    let serverVersion = 5;
    mocks.api
      .mockRejectedValueOnce(
        new ApiError(409, 'work_order_version_conflict', 'Changed by another user.'),
      )
      .mockResolvedValueOnce({ workOrderVersion: 6 });
    const f = fixture(async () => serverVersion);
    await act(async () => {
      await expect(f.result.current.a.mutateAsync(command(4))).rejects.toMatchObject({
        status: 409,
      });
    });
    expect(f.version()).toBe(4);
    expect(mocks.api).toHaveBeenCalledTimes(1);
    await act(async () => {
      await f.result.current.a.reload();
    });
    expect(f.version()).toBe(5);
    serverVersion = 6;
    await act(async () => {
      await f.result.current.a.mutateAsync(command(5));
    });
    expect(mocks.api.mock.calls.map(([, options]) => options.body.expectedVersion)).toEqual([4, 5]);
  });
  it('preserves cache on failed writes and allows an explicit retry with the same version', async () => {
    mocks.api
      .mockRejectedValueOnce(new ApiError(500, 'unavailable', 'Unavailable.'))
      .mockResolvedValueOnce({ workOrderVersion: 5 });
    const f = fixture();
    await act(async () => {
      await expect(f.result.current.a.mutateAsync(command(4))).rejects.toMatchObject({
        status: 500,
      });
    });
    expect(f.version()).toBe(4);
    expect(f.result.current.state.busy).toBe(false);
    await act(async () => {
      await f.result.current.a.mutateAsync(command(4));
    });
    expect(f.version()).toBe(5);
  });
  it('retains acknowledged data when reconciliation fails and recovers without replaying the write', async () => {
    let available = false;
    mocks.api.mockResolvedValueOnce({ workOrderVersion: 5 });
    const f = fixture(async () => {
      if (!available) throw new Error('Detail temporarily unavailable.');
      return 5;
    });
    await act(async () => {
      await expect(f.result.current.a.mutateAsync(command(4))).rejects.toThrow(
        'Detail temporarily unavailable.',
      );
    });
    expect(f.version()).toBe(5);
    expect(f.client.getQueryState(key)?.status).toBe('error');
    expect(f.result.current.state.busy).toBe(false);
    expect(mocks.api).toHaveBeenCalledTimes(1);
    available = true;
    await act(async () => {
      await f.result.current.a.reload();
    });
    expect(f.client.getQueryState(key)?.status).toBe('success');
    expect(f.version()).toBe(5);
    expect(mocks.api).toHaveBeenCalledTimes(1);
    // A failed reconciliation must also release the shared write lock.
    mocks.api.mockRejectedValueOnce(new ApiError(503, 'unavailable', 'Try again later.'));
    await act(async () => {
      await expect(f.result.current.b.mutateAsync(command(5))).rejects.toMatchObject({
        status: 503,
      });
    });
    expect(mocks.api).toHaveBeenCalledTimes(2);
    expect(f.version()).toBe(5);
  });
  it('holds the aggregate lock through refetch even after another blocked writer settles', async () => {
    let finish: (version: number) => void = () => undefined;
    mocks.api.mockResolvedValueOnce({ workOrderVersion: 5 });
    const f = fixture(
      () =>
        new Promise<number>((resolve) => {
          finish = resolve;
        }),
    );
    let first: Promise<unknown>;
    await act(async () => {
      first = f.result.current.a.mutateAsync(command(4));
    });
    await waitFor(() => expect(f.version()).toBe(5));
    expect(f.result.current.state.busy).toBe(true);
    for (let attempt = 0; attempt < 2; attempt++) {
      await act(async () => {
        await expect(f.result.current.b.mutateAsync(command(5))).rejects.toMatchObject({
          code: 'update_in_progress',
        });
      });
    }
    expect(mocks.api).toHaveBeenCalledTimes(1);
    await act(async () => {
      finish(5);
      await first!;
    });
    await waitFor(() => expect(f.result.current.state.busy).toBe(false));
    expect(f.version()).toBe(5);
  });
  it('rejects a stale edited snapshot even if a background read already knows a newer version', async () => {
    const f = fixture();
    f.client.setQueryData(key, { order: { version: 5 } });
    await act(async () => {
      await expect(f.result.current.a.mutateAsync(command(4))).rejects.toMatchObject({
        status: 409,
        code: 'work_order_version_conflict',
      });
    });
    expect(mocks.api).not.toHaveBeenCalled();
  });
});

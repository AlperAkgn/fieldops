import { afterEach, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { WorkOrderDetailPage } from './WorkOrdersPage';
import type { OrderDetail, WorkOrder } from '../api/contracts';

const state = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('../api/client', async (original) => ({
  ...(await original()),
  api: (...args: unknown[]) => state.api(...args),
}));
vi.mock('../api/session', () => ({
  useSession: () => ({
    tenantId: 'tenant',
    userId: 'manager',
    role: 'Manager',
    sessionId: 'session',
  }),
  canManage: () => true,
}));
afterEach(() => vi.clearAllMocks());
const order = {
  id: 'order',
  title: 'Inspection',
  workOrderNumber: 'WO-1',
  version: 4,
  status: 'Assigned',
  priority: 'Medium',
  createdAt: '2026-11-07T10:00:00Z',
  updatedAt: '2026-11-07T10:00:00Z',
} as WorkOrder;
const detail = (version: number): OrderDetail => ({
  order: { ...order, version },
  customer: { id: 'customer', name: 'Customer' },
  serviceLocation: { id: 'location', name: 'Site', addressLine: 'Road', city: 'City' },
  asset: null,
  assignments: [],
});

it('blocks Start until checklist acknowledgement and detail refetch synchronize the aggregate version', async () => {
  let version = 4;
  let hold = true;
  let status: WorkOrder['status'] = 'Assigned';
  let finishRefetch: (() => void) | undefined;
  const writes: { path: string; body: { expectedVersion: number } }[] = [];
  state.api.mockImplementation(
    async (path: string, options?: { method?: string; body?: { expectedVersion: number } }) => {
      if (options?.method) {
        writes.push({ path, body: options.body! });
        if (path.includes('/checklist/')) {
          version++;
          return {
            workOrderId: 'order',
            workOrderVersion: version,
            progressPercent: 100,
            items: [{ id: 'step', label: 'Check filter', isRequired: true, isCompleted: true }],
          };
        }
        version++;
        status = 'InProgress';
        return { ...order, version, status };
      }
      if (path === '/work-orders/order') {
        if (version === 4) return detail(4);
        if (!hold) return { ...detail(version), order: { ...order, version, status } };
        return new Promise<OrderDetail>((resolve) => {
          finishRefetch = () => {
            hold = false;
            resolve(detail(version));
          };
        });
      }
      if (path.endsWith('/checklist'))
        return {
          workOrderId: 'order',
          workOrderVersion: version,
          progressPercent: version === 4 ? 0 : 100,
          items: [
            { id: 'step', label: 'Check filter', isRequired: true, isCompleted: version > 4 },
          ],
        };
      return { items: [], page: 1, pageSize: 10, totalCount: 0 };
    },
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 10000 } },
  });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/work-orders/order']}>
        <Routes>
          <Route path="/work-orders/:id" element={<WorkOrderDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  await userEvent.click(await screen.findByLabelText(/Check filter/));
  await waitFor(() => expect(finishRefetch).toBeTypeOf('function'));
  const start = screen.getByRole('button', { name: 'Start work order' });
  expect((start as HTMLButtonElement).disabled).toBe(true);
  await act(async () => finishRefetch!());
  await waitFor(() => expect((start as HTMLButtonElement).disabled).toBe(false));
  expect(screen.getByText(/Version 5/)).toBeTruthy();
  HTMLDialogElement.prototype.showModal = function () {
    this.setAttribute('open', '');
  };
  HTMLDialogElement.prototype.close = function () {
    this.removeAttribute('open');
  };
  await userEvent.click(start);
  await userEvent.click(screen.getByRole('button', { name: 'Confirm start' }));
  await waitFor(() =>
    expect(writes.find((write) => write.path.endsWith('/start'))?.body.expectedVersion).toBe(5),
  );
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  client.clear();
  expect(writes[0]?.body.expectedVersion).toBe(4);
});

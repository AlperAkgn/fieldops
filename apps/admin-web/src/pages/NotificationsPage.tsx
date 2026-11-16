import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import type { Page } from '../api/contracts';
import { useSession } from '../api/session';
import { dateTime } from '../api/hooks';
import { ErrorBox, PageHeader, Pagination, Panel } from '../components/ui';

type Item = {
  id: string;
  workOrderId: string | null;
  kind: string;
  createdAt: string;
  readAt: string | null;
};

export function NotificationsPage() {
  const session = useSession();
  const [page, setPage] = useState(1);
  const client = useQueryClient();
  const scope = [
    session?.tenantId,
    session?.userId,
    session?.role,
    session?.sessionId,
    'notifications',
  ];
  const query = useQuery({
    queryKey: [...scope, page],
    refetchInterval: 30_000,
    queryFn: ({ signal }) => api<Page<Item>>(`/notifications?page=${page}&pageSize=25`, { signal }),
  });
  const read = useMutation({
    mutationFn: (id: string) => api(`/notifications/${id}/read`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: scope }),
  });
  return (
    <>
      <PageHeader title="Notifications" />
      <Panel>
        {query.isPending && <p role="status">Loading notifications…</p>}
        {query.error && <ErrorBox error={query.error} retry={() => void query.refetch()} />}
        {read.error && <ErrorBox error={read.error} />}
        {query.data?.items.length === 0 && <p>No workspace notifications.</p>}
        {query.data?.items.map((item) => (
          <div className="person-row" key={item.id}>
            <div>
              <strong>{item.kind.replaceAll('_', ' ')}</strong>
              <small>{dateTime(item.createdAt)}</small>
            </div>
            {item.workOrderId && (
              <Link to={`/work-orders/${item.workOrderId}`}>Open work order</Link>
            )}
            {!item.readAt && (
              <button
                className="btn secondary"
                disabled={read.isPending}
                onClick={() => read.mutate(item.id)}
              >
                Mark read
              </button>
            )}
          </div>
        ))}
        {query.data && (
          <Pagination page={page} size={25} total={query.data.totalCount} onChange={setPage} />
        )}
      </Panel>
    </>
  );
}

import { useEffect, useState, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { refresh } from './api/client';
import { useSession, canManage } from './api/session';
import { Empty, Skeleton } from './components/ui';

export function Bootstrap({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const client = useQueryClient();
  useEffect(() => {
    const start = () => {
      client.clear();
      setReady(false);
      void refresh()
        .catch(() => undefined)
        .finally(() => setReady(true));
    };
    start();
    window.addEventListener('fieldops-session-changed', start);
    return () => window.removeEventListener('fieldops-session-changed', start);
  }, [client]);
  return ready ? (
    children
  ) : (
    <div className="boot">
      <Skeleton />
    </div>
  );
}
export function Protected({
  children,
  manager = false,
  owner = false,
}: {
  children: ReactNode;
  manager?: boolean;
  owner?: boolean;
}) {
  const session = useSession();
  const location = useLocation();
  if (!session) return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  if ((manager && !canManage(session.role)) || (owner && session.role !== 'OrganizationOwner'))
    return (
      <Empty
        title="Access restricted"
        text="Your organization role does not permit this page. Contact an organization owner."
      />
    );
  return children;
}

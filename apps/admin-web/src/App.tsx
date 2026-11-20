import { canManage, useSession } from './api/session';
import { lazy, Suspense } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Bootstrap, Protected } from './auth';
import { ToastProvider, Empty, Skeleton } from './components/ui';
import { Layout } from './components/Layout';
import { AuthPage } from './pages/AuthPage';
const CatalogPage = lazy(() =>
  import('./pages/CatalogPage').then((page) => ({ default: page.CatalogPage })),
);
const CatalogDetail = lazy(() =>
  import('./pages/CatalogPage').then((page) => ({ default: page.CatalogDetail })),
);
const WorkOrdersPage = lazy(() =>
  import('./pages/WorkOrdersPage').then((page) => ({ default: page.WorkOrdersPage })),
);
const WorkOrderDetailPage = lazy(() =>
  import('./pages/WorkOrdersPage').then((page) => ({ default: page.WorkOrderDetailPage })),
);
const OverviewPage = lazy(() =>
  import('./pages/OverviewPage').then((module) => ({ default: module.OverviewPage })),
);
const SchedulingPage = lazy(() =>
  import('./pages/SchedulingPage').then((page) => ({ default: page.SchedulingPage })),
);
const NotificationsPage = lazy(() =>
  import('./pages/NotificationsPage').then((page) => ({ default: page.NotificationsPage })),
);
const TeamPage = lazy(() =>
  import('./pages/ManagementPages').then((page) => ({ default: page.TeamPage })),
);
const SettingsPage = lazy(() =>
  import('./pages/ManagementPages').then((page) => ({ default: page.SettingsPage })),
);
const TemplatesPage = lazy(() =>
  import('./pages/ManagementPages').then((page) => ({ default: page.TemplatesPage })),
);
function Home() {
  const session = useSession();
  return <Navigate to={canManage(session?.role) ? '/overview' : '/work-orders'} replace />;
}
export default function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <Bootstrap>
          <Suspense fallback={<Skeleton />}>
            <Routes>
              {['login', 'register', 'activate', 'forgot-password', 'reset-password'].map(
                (mode) => (
                  <Route
                    key={mode}
                    path={`/${mode}`}
                    element={<AuthPage key={mode} mode={mode} />}
                  />
                ),
              )}
              <Route
                element={
                  <Protected>
                    <Layout />
                  </Protected>
                }
              >
                <Route index element={<Home />} />
                <Route
                  path="overview"
                  element={
                    <Protected manager>
                      <OverviewPage />
                    </Protected>
                  }
                />
                <Route
                  path="reports"
                  element={
                    <Protected manager>
                      <OverviewPage reports />
                    </Protected>
                  }
                />
                <Route path="work-orders" element={<WorkOrdersPage />} />
                <Route path="work-orders/:id" element={<WorkOrderDetailPage />} />
                <Route path="notifications" element={<NotificationsPage />} />
                {(['customers', 'service-locations', 'assets', 'asset-categories'] as const).map(
                  (kind) => (
                    <Route key={kind} path={kind} element={<CatalogPage kind={kind} />} />
                  ),
                )}
                {(['customers', 'service-locations', 'assets', 'asset-categories'] as const).map(
                  (kind) => (
                    <Route
                      key={kind}
                      path={`${kind}/:id`}
                      element={<CatalogDetail kind={kind} />}
                    />
                  ),
                )}
                <Route
                  path="scheduling"
                  element={
                    <Protected manager>
                      <SchedulingPage />
                    </Protected>
                  }
                />
                <Route
                  path="team"
                  element={
                    <Protected manager>
                      <TeamPage />
                    </Protected>
                  }
                />
                <Route
                  path="checklist-templates"
                  element={
                    <Protected manager>
                      <TemplatesPage />
                    </Protected>
                  }
                />
                <Route path="settings" element={<SettingsPage />} />
                <Route
                  path="*"
                  element={
                    <Empty
                      title="Page not found"
                      text="Choose a page from the navigation to continue."
                    />
                  }
                />
              </Route>
            </Routes>
          </Suspense>
        </Bootstrap>
      </ToastProvider>
    </BrowserRouter>
  );
}

import { canManage, useSession } from './api/session';
import { lazy, Suspense } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Bootstrap, Protected } from './auth';
import { ToastProvider, Empty, Skeleton } from './components/ui';
import { Layout } from './components/Layout';
import { AuthPage } from './pages/AuthPage';
import { CatalogPage, CatalogDetail } from './pages/CatalogPage';
import { WorkOrdersPage, WorkOrderDetailPage } from './pages/WorkOrdersPage';
const OverviewPage = lazy(() =>
  import('./pages/OverviewPage').then((module) => ({ default: module.OverviewPage })),
);
import { SchedulingPage } from './pages/SchedulingPage';
import { TeamPage, SettingsPage, TemplatesPage } from './pages/ManagementPages';
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

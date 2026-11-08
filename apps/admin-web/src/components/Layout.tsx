import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Activity,
  ArrowUpRight,
  BarChart3,
  Building2,
  CalendarDays,
  ChevronDown,
  ClipboardList,
  Layers3,
  LogOut,
  Menu,
  Moon,
  Search,
  Settings2,
  ShieldCheck,
  Sun,
  Users,
  Wrench,
  X,
} from 'lucide-react';
import { api, logout, selectOrganization, updateRole } from '../api/client';
import type { Organization, Profile } from '../api/contracts';
import { canManage, useSession } from '../api/session';
import { useToast } from './ui-state';

const nav = [
  { to: '/overview', label: 'Overview', icon: Activity, manager: true },
  { to: '/work-orders', label: 'Work orders', icon: ClipboardList },
  { to: '/scheduling', label: 'Schedule', icon: CalendarDays, manager: true },
  { to: '/customers', label: 'Customers', icon: Building2, catalog: true },
  { to: '/service-locations', label: 'Service locations', icon: Layers3, catalog: true },
  { to: '/assets', label: 'Assets', icon: Wrench, catalog: true },
  { to: '/team', label: 'Team', icon: Users, manager: true },
  { to: '/reports', label: 'Reports', icon: BarChart3, manager: true },
];
export function Layout() {
  const session = useSession();
  const client = useQueryClient();
  const toast = useToast();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [mobile, setMobile] = useState(() => window.matchMedia('(max-width: 760px)').matches);
  useEffect(() => {
    const media = window.matchMedia('(max-width: 760px)');
    const resize = () => {
      const sidebar = document.getElementById('workspace-navigation');
      if (media.matches && sidebar?.contains(document.activeElement))
        document.querySelector<HTMLButtonElement>('.mobile-menu')?.focus();
      setMobile(media.matches);
      setOpen(false);
    };
    media.addEventListener('change', resize);
    return () => media.removeEventListener('change', resize);
  }, []);
  const [busy, setBusy] = useState(false);
  const [dark, setDark] = useState(() => localStorage.getItem('fieldops-theme') === 'dark');
  const organizations = useQuery({
    queryKey: [
      session?.tenantId,
      session?.userId,
      session?.role,
      session?.sessionId,
      'organizations',
    ],
    queryFn: ({ signal }) => api<Organization[]>('/organizations', { signal }),
  });
  const profile = useQuery({
    queryKey: [session?.tenantId, session?.userId, session?.role, session?.sessionId, 'me'],
    queryFn: ({ signal }) => api<Profile>('/auth/me', { signal }),
    refetchInterval: 60000,
    refetchOnWindowFocus: true,
  });
  useEffect(() => {
    if (profile.data) updateRole(profile.data.role);
  }, [profile.data]);
  useEffect(() => {
    if (!open) return;
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setOpen(false);
      }
      if (event.key === 'Tab') {
        const nodes = Array.from(
          document.querySelectorAll<HTMLElement>(
            '#workspace-navigation a[href], #workspace-navigation button:not(:disabled), #workspace-navigation select:not(:disabled)',
          ),
        ).filter((node) => node.getClientRects().length > 0);
        const first = nodes[0];
        const last = nodes.at(-1);
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last?.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first?.focus();
        }
      }
    };
    document.addEventListener('keydown', escape);
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.querySelector<HTMLButtonElement>('.mobile-close')?.focus();
    return () => {
      document.removeEventListener('keydown', escape);
      document.body.style.overflow = previousOverflow;
      if (window.matchMedia('(max-width: 760px)').matches)
        document.querySelector<HTMLButtonElement>('.mobile-menu')?.focus();
      else
        document
          .querySelector<HTMLAnchorElement>(
            '#workspace-navigation a.active, #workspace-navigation a',
          )
          ?.focus();
    };
  }, [open]);
  useEffect(() => {
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
    localStorage.setItem('fieldops-theme', dark ? 'dark' : 'light');
  }, [dark]);
  async function changeOrganization(id: string) {
    setBusy(true);
    try {
      await client.cancelQueries();
      const selected = await selectOrganization(id);
      client.clear();
      navigate(canManage(selected.role) ? '/overview' : '/work-orders');
      setOpen(false);
      toast('Organization switched.');
    } catch (error) {
      toast(error instanceof Error ? error.message : 'Unable to switch organization.');
    } finally {
      setBusy(false);
    }
  }
  async function signOut() {
    setBusy(true);
    try {
      await logout();
      client.clear();
      navigate('/login');
    } catch (error) {
      toast(error instanceof Error ? error.message : 'Sign out failed. Try again.');
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      {open && (
        <button
          className="sidebar-backdrop"
          aria-label="Close navigation"
          aria-hidden="true"
          tabIndex={-1}
          onClick={() => setOpen(false)}
        />
      )}
      <aside
        id="workspace-navigation"
        inert={mobile && !open}
        aria-hidden={(mobile && !open) || undefined}
        aria-label="Workspace navigation"
        role={mobile && open ? 'dialog' : undefined}
        aria-modal={(mobile && open) || undefined}
        className={`sidebar ${open ? 'is-open' : ''}`}
      >
        <Link className="brand" to="/">
          <span className="brand-mark">
            <Layers3 size={23} />
          </span>
          fieldops<span className="brand-dot">.</span>
        </Link>
        <button
          className="mobile-close btn secondary icon"
          aria-label="Close navigation"
          onClick={() => setOpen(false)}
        >
          <X size={20} />
        </button>
        <div className="workspace-switch">
          <span className="workspace-avatar">
            {organizations.data?.find((org) => org.id === session?.tenantId)?.name.slice(0, 1) ||
              'F'}
          </span>
          <div>
            <small>WORKSPACE</small>
            <label className="sr-only" htmlFor="organization">
              Active organization
            </label>
            <select
              id="organization"
              disabled={busy || organizations.isPending}
              value={session?.tenantId}
              onChange={(event) => {
                void changeOrganization(event.target.value);
              }}
            >
              {organizations.data?.map((org) => (
                <option key={org.id} value={org.id}>
                  {org.name}
                </option>
              ))}
            </select>
          </div>
          <ChevronDown size={14} />
        </div>
        {organizations.isError && (
          <small role="alert">Organizations unavailable. Reload to retry.</small>
        )}
        <div className="nav-label">WORKSPACE</div>
        <nav aria-label="Main navigation">
          {nav
            .filter(
              (item) =>
                (!item.manager || canManage(session?.role)) &&
                (!item.catalog || session?.role !== 'Technician'),
            )
            .map((item) => (
              <NavLink key={item.to} to={item.to} onClick={() => setOpen(false)}>
                <item.icon size={19} />
                {item.label}
                {item.to === '/work-orders' && (
                  <span className="nav-key" aria-hidden="true">
                    W
                  </span>
                )}
              </NavLink>
            ))}
        </nav>
        {canManage(session?.role) && (
          <>
            <div className="nav-label">CONFIGURATION</div>
            <nav aria-label="Configuration">
              <NavLink to="/asset-categories" onClick={() => setOpen(false)}>
                <Layers3 size={18} />
                Asset categories
              </NavLink>
              <NavLink to="/checklist-templates" onClick={() => setOpen(false)}>
                <ClipboardList size={18} />
                Checklist templates
              </NavLink>
            </nav>
          </>
        )}
        <div className="sidebar-footer">
          <div className="security-note">
            <ShieldCheck size={20} />
            <span>
              Secure workspace<small>Tenant-isolated operations</small>
            </span>
          </div>
          <NavLink to="/settings" onClick={() => setOpen(false)}>
            <Settings2 size={18} />
            Settings
          </NavLink>
          <button
            className="signout"
            onClick={() => {
              void signOut();
            }}
            disabled={busy}
          >
            <LogOut size={18} />
            Sign out
          </button>
        </div>
      </aside>
      <div className="app-body" inert={mobile && open}>
        <header className="topbar">
          <div className="topbar-left">
            <button
              className="btn secondary icon mobile-menu"
              aria-label="Open navigation"
              aria-expanded={open}
              aria-controls="workspace-navigation"
              onClick={() => setOpen(true)}
            >
              <Menu size={21} />
            </button>
            <span className="breadcrumb">
              Workspace <span>/</span> Operations
            </span>
          </div>
          <div className="topbar-right">
            <Link className="quick-search" to="/work-orders">
              <Search size={16} />
              <span>Find a work order</span>
              <ArrowUpRight size={14} />
            </Link>
            <button
              className="btn secondary icon theme-toggle"
              aria-label={dark ? 'Use light theme' : 'Use dark theme'}
              onClick={() => setDark(!dark)}
            >
              {dark ? <Sun size={18} /> : <Moon size={18} />}
            </button>
            <Link className="user-chip" to="/settings">
              <span className="avatar">{profile.data?.displayName.slice(0, 1) || 'U'}</span>
              <span>
                {profile.data?.displayName || 'Your account'}
                <small>{session?.role.replace('OrganizationOwner', 'Owner')}</small>
              </span>
            </Link>
          </div>
        </header>
        <main id="main" tabIndex={-1}>
          <Outlet />
        </main>
        <footer className="app-footer">
          FieldOps workspace<span>Built for the work ahead.</span>
        </footer>
      </div>
    </div>
  );
}

import { getApiOrigin } from './health';
import type { Access } from './contracts';

function apiOrigin() {
  const origin = getApiOrigin(import.meta.env.VITE_API_BASE_URL || 'https://localhost:5443');
  if (!origin.startsWith('https://'))
    throw new Error('Authentication requires an HTTPS API origin.');
  return origin;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public traceId?: string,
  ) {
    super(message);
  }
}
let access: Access | null = null;
let refreshPromise: Promise<Access> | null = null;
let queue: Promise<unknown> = Promise.resolve();
let generation = 0;
const listeners = new Set<() => void>();
const channel =
  typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('fieldops-session');
export const sessionStore = {
  getSnapshot: () => access,
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};
export function updateRole(role: Access['role']) {
  if (access && access.role !== role) publish({ ...access, role });
}
function publish(value: Access | null) {
  access = value;
  listeners.forEach((listener) => listener());
}
function clear() {
  generation++;
  publish(null);
}
channel?.addEventListener('message', () => {
  clear();
  window.dispatchEvent(new Event('fieldops-session-changed'));
});

async function raw<T>(path: string, init: RequestInit = {}, token?: string): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Accept', 'application/json');
  if (init.body) headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const timeout = AbortSignal.timeout(20000);
  const response = await fetch(`${apiOrigin()}/api/v1${path}`, {
    ...init,
    headers,
    credentials: 'include',
    cache: 'no-store',
    signal: init.signal ? AbortSignal.any([init.signal, timeout]) : timeout,
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as {
      code?: string;
      detail?: string;
      title?: string;
      traceId?: string;
    };
    throw new ApiError(
      response.status,
      problem.code || 'request_failed',
      problem.detail || problem.title || `Request failed (${response.status}).`,
      problem.traceId,
    );
  }
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>);
}
async function serialized<T>(operation: () => Promise<T>): Promise<T> {
  const run = () =>
    typeof navigator !== 'undefined' && navigator.locks
      ? navigator.locks.request('fieldops-cookie-session', operation)
      : operation();
  const next = queue.then(run, run);
  queue = next.catch(() => undefined);
  return next;
}
async function web<T>(path: string, body?: unknown, token?: string): Promise<T> {
  const csrf = await raw<{ token: string }>('/auth/web/csrf', {}, token);
  return raw<T>(
    `/auth/web${path}`,
    {
      method: 'POST',
      headers: { 'X-CSRF-TOKEN': csrf.token },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    },
    token,
  );
}
export function refresh(): Promise<Access> {
  if (refreshPromise) return refreshPromise;
  refreshPromise = serialized(async () => {
    try {
      const result = await web<Access>('/refresh');
      publish(result);
      return result;
    } catch (error) {
      clear();
      throw error;
    }
  }).finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}
export async function login(body: unknown) {
  return serialized(async () => {
    const result = await web<Access>('/login', body);
    generation++;
    publish(result);
    channel?.postMessage('changed');
    return result;
  });
}
export const register = (body: unknown) => serialized(() => web('/register', body));
export const publicPost = (path: string, body: unknown) =>
  raw(`/auth/${path}`, { method: 'POST', body: JSON.stringify(body) });
export async function logout() {
  if (access && Date.parse(access.expiresAt) < Date.now() + 30000) await refresh();
  return serialized(async () => {
    if (access) await web('/logout', undefined, access.accessToken);
    clear();
    channel?.postMessage('changed');
  });
}
export async function selectOrganization(id: string) {
  return serialized(async () => {
    if (!access) throw new ApiError(401, 'session_required', 'Please sign in again.');
    const result = await web<Access>(`/organizations/${id}/select`, undefined, access.accessToken);
    generation++;
    publish(result);
    channel?.postMessage('changed');
    return result;
  });
}
export async function api<T>(
  path: string,
  options: { method?: string; body?: unknown; signal?: AbortSignal } = {},
): Promise<T> {
  if (!access) throw new ApiError(401, 'session_required', 'Please sign in again.');
  if (Date.parse(access.expiresAt) < Date.now() + 30000) await refresh();
  const current = access;
  const started = generation;
  if (!current) throw new ApiError(401, 'session_required', 'Please sign in again.');
  try {
    const result = await raw<T>(
      path,
      {
        method: options.method || 'GET',
        ...(options.body === undefined ? {} : { body: JSON.stringify(options.body) }),
        ...(options.signal ? { signal: options.signal } : {}),
      },
      current.accessToken,
    );
    if (started !== generation) throw new DOMException('Organization changed.', 'AbortError');
    return result;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401 && started === generation) clear();
    throw error;
  }
}
export function queryString(values: Record<string, string | number | boolean | undefined | null>) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  });
  return params.toString();
}

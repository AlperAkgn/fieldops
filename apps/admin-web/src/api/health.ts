export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy';
export type HealthEndpoint = 'live' | 'ready';
export interface HealthReport { status: HealthStatus }

export function getApiOrigin(baseUrl: string): string {
  let url: URL;
  try {
    url = new URL(baseUrl);
  } catch {
    throw new Error('Configure a valid HTTP(S) API origin.');
  }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password
    || url.pathname !== '/' || url.search || url.hash) {
    throw new Error('Configure an HTTP(S) API origin without credentials, a path, query, or fragment.');
  }
  return url.origin;
}

export async function fetchHealth(
  baseUrl: string,
  endpoint: HealthEndpoint,
  signal?: AbortSignal,
): Promise<HealthReport> {
  if (signal?.aborted) {
    const error = new Error('Health check cancelled.');
    error.name = 'AbortError';
    throw error;
  }
  const apiOrigin = getApiOrigin(baseUrl);
  const controller = new AbortController();
  const abort = () => controller.abort(signal?.reason);
  signal?.addEventListener('abort', abort, { once: true });
  const timeout = setTimeout(() => controller.abort(), 5_000);

  try {
    const url = new URL(`/health/${endpoint}`, apiOrigin);
    const response = await fetch(url.toString(), { signal: controller.signal, cache: 'no-store', headers: { Accept: 'application/json' } });
    if (response.status !== 200 && response.status !== 503) throw new Error(`Health check failed (${response.status}).`);
    const payload: unknown = await response.json();
    if (typeof payload !== 'object' || payload === null || !('status' in payload)
      || typeof payload.status !== 'string'
      || !['Healthy', 'Degraded', 'Unhealthy'].includes(payload.status)
      || (response.status === 503) !== (payload.status === 'Unhealthy')) {
      throw new Error('The API returned an invalid health response.');
    }
    return { status: payload.status as HealthStatus };
  } finally {
    clearTimeout(timeout);
    signal?.removeEventListener('abort', abort);
  }
}

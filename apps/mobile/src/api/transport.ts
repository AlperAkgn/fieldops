import { getApiOrigin } from './health';
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public retryAfter?: number,
  ) {
    super(message);
  }
}
export function nativeOrigin(value: string, localDevelopment = false) {
  const origin = getApiOrigin(value);
  const { hostname, protocol } = new URL(origin);
  const local =
    hostname === 'localhost' ||
    hostname === '127.0.0.1' ||
    hostname === '10.0.2.2' ||
    /^10\.\d+\.\d+\.\d+$/.test(hostname) ||
    /^192\.168\.\d+\.\d+$/.test(hostname) ||
    /^172\.(1[6-9]|2\d|3[01])\.\d+\.\d+$/.test(hostname);
  if (protocol !== 'https:' && !(localDevelopment && local))
    throw new Error(
      'Use an HTTPS API origin. Local HTTP requires an explicit development setting.',
    );
  return origin;
}
export type RequestOptions = {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
  token?: string;
};
export class NativeTransport {
  constructor(
    readonly origin: string,
    private fetcher: typeof fetch = fetch,
    private timeoutMs = 15000,
  ) {}
  async request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    if (!path.startsWith('/') || path.startsWith('//') || /[\r\n]/.test(path))
      throw new Error('Invalid API path.');
    const controller = new AbortController();
    const cancel = () => controller.abort();
    if (options.signal?.aborted) controller.abort();
    options.signal?.addEventListener('abort', cancel, { once: true });
    const timer = setTimeout(cancel, this.timeoutMs);
    try {
      const response = await this.fetcher(`${this.origin}/api/v1${path}`, {
        method: options.method || 'GET',
        credentials: 'omit',
        headers: {
          Accept: 'application/json',
          ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
          ...(options.token ? { Authorization: `Bearer ${options.token}` } : {}),
        },
        ...(options.body === undefined ? {} : { body: JSON.stringify(options.body) }),
        signal: controller.signal,
      });
      if (!response.ok) {
        const value: unknown = await response.json().catch(() => null);
        const problem =
          value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
        const text = (candidate: unknown) =>
          typeof candidate === 'string' && candidate.length <= 4000 ? candidate : undefined;
        const retryAfter = Number(response.headers.get('Retry-After'));
        throw new ApiError(
          response.status,
          text(problem.code) || 'request_failed',
          text(problem.detail) || text(problem.title) || `Request failed (${response.status}).`,
          retryAfter > 0 ? retryAfter : undefined,
        );
      }
      return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
    } catch (error) {
      if (controller.signal.aborted && !options.signal?.aborted)
        throw new ApiError(
          0,
          'request_timeout',
          'The request timed out. Reload current data before retrying a change.',
        );
      throw error;
    } finally {
      clearTimeout(timer);
      options.signal?.removeEventListener('abort', cancel);
    }
  }
}
export function queryString(values: Record<string, string | number | boolean | undefined>) {
  return Object.entries(values)
    .filter(([, value]) => value !== undefined && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
    .join('&');
}

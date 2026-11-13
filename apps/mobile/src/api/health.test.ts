import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchHealth, getApiOrigin } from './health';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('health transport', () => {
  it('requests the selected API endpoint and validates a healthy response', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ status: 'Healthy' })));
    vi.stubGlobal('fetch', fetchMock);
    await expect(fetchHealth('http://localhost:5080/', 'ready')).resolves.toEqual({
      status: 'Healthy',
    });
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe('http://localhost:5080/health/ready');
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ headers: { Accept: 'application/json' } });
  });

  it('accepts an unhealthy readiness report returned with HTTP 503', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify({ status: 'Unhealthy' }), { status: 503 })),
    );
    await expect(fetchHealth('http://localhost:5080', 'ready')).resolves.toEqual({
      status: 'Unhealthy',
    });
  });

  it('rejects other server failures without exposing their response bodies', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('private server details', { status: 500 })),
    );
    await expect(fetchHealth('http://localhost:5080', 'live')).rejects.toThrow(
      'Health check failed (500).',
    );
  });

  it.each(['Unknown', ['Healthy'], { value: 'Healthy' }, null, 200])(
    'rejects an invalid health status: %j',
    async (status) => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ status }))));
      await expect(fetchHealth('http://localhost:5080', 'live')).rejects.toThrow(
        'invalid health response',
      );
    },
  );

  it.each([
    [503, 'Healthy'],
    [503, 'Degraded'],
    [200, 'Unhealthy'],
  ] as const)(
    'rejects a contradictory HTTP %i / %s health response',
    async (httpStatus, status) => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(new Response(JSON.stringify({ status }), { status: httpStatus })),
      );
      await expect(fetchHealth('http://localhost:5080', 'ready')).rejects.toThrow(
        'invalid health response',
      );
    },
  );

  it('accepts a degraded report returned with HTTP 200', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'Degraded' }))),
    );
    await expect(fetchHealth('http://localhost:5080', 'ready')).resolves.toEqual({
      status: 'Degraded',
    });
  });

  it.each([
    'invalid',
    'file:///etc/passwd',
    'http://user:password@localhost:5080',
    'http://localhost:5080/api',
    'http://localhost:5080?token=secret',
    'http://localhost:5080#secret',
  ])(
    'rejects invalid or sensitive API configuration before making a request: %s',
    async (baseUrl) => {
      const fetchMock = vi.fn();
      vi.stubGlobal('fetch', fetchMock);
      expect(() => getApiOrigin(baseUrl)).toThrow('Configure');
      await expect(fetchHealth(baseUrl, 'live')).rejects.toThrow('Configure');
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it('normalizes a valid origin for safe display and query keys', () => {
    expect(getApiOrigin('http://LOCALHOST:5080/')).toBe('http://localhost:5080');
  });

  it('preserves network failures for the UI error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Network request failed')));
    await expect(fetchHealth('http://localhost:5080', 'live')).rejects.toThrow(
      'Network request failed',
    );
  });

  it('cancels the network request when its caller cancels', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url: unknown, init: RequestInit) =>
          new Promise<Response>((_resolve, reject) => {
            init.signal?.addEventListener('abort', () =>
              reject(new DOMException('Aborted', 'AbortError')),
            );
          }),
      ),
    );
    const controller = new AbortController();
    const request = fetchHealth('http://localhost:5080', 'live', controller.signal);
    const assertion = expect(request).rejects.toMatchObject({ name: 'AbortError' });
    controller.abort();
    await assertion;
  });

  it('does not issue a network request when already cancelled', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();
    controller.abort();
    await expect(
      fetchHealth('http://localhost:5080', 'live', controller.signal),
    ).rejects.toMatchObject({ name: 'AbortError' });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('aborts a stalled request after five seconds', async () => {
    vi.useFakeTimers();
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url: unknown, init: RequestInit) =>
          new Promise<Response>((_resolve, reject) => {
            init.signal?.addEventListener('abort', () =>
              reject(new DOMException('Aborted', 'AbortError')),
            );
          }),
      ),
    );
    const assertion = expect(fetchHealth('http://localhost:5080', 'ready')).rejects.toMatchObject({
      name: 'AbortError',
    });
    await vi.advanceTimersByTimeAsync(5_000);
    await assertion;
  });
});

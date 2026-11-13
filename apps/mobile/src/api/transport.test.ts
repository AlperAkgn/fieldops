import { describe, expect, it, vi } from 'vitest';
import { NativeTransport, nativeOrigin, queryString } from './transport';
describe('native transport', () => {
  it.each([null, 'invalid', { detail: { unexpected: true } }])(
    'handles malformed public problem bodies safely: %j',
    async (value) => {
      const transport = new NativeTransport('https://api.test', async () =>
        Response.json(value, { status: 500 }),
      );
      await expect(transport.request('/work-orders')).rejects.toMatchObject({
        status: 500,
        code: 'request_failed',
        message: 'Request failed (500).',
      });
    },
  );
  it('keeps its timeout active while reading the response body', async () => {
    const transport = new NativeTransport(
      'https://api.test',
      async (_url, init) =>
        new Response(
          new ReadableStream({
            start(controller) {
              init?.signal?.addEventListener('abort', () =>
                controller.error(new DOMException('Aborted', 'AbortError')),
              );
            },
          }),
        ),
      5,
    );
    await expect(transport.request('/work-orders')).rejects.toMatchObject({
      code: 'request_timeout',
    });
  });
  it('requires TLS except explicitly opted in private development addresses', () => {
    expect(nativeOrigin('https://api.test')).toBe('https://api.test');
    expect(() => nativeOrigin('http://api.test', true)).toThrow();
    expect(() => nativeOrigin('http://10.0.2.2:5080')).toThrow();
    expect(nativeOrigin('http://10.0.2.2:5080', true)).toBe('http://10.0.2.2:5080');
    expect(() => nativeOrigin('https://user:pass@api.test')).toThrow();
  });
  it('uses bearer JSON and no cookies or web transport headers', async () => {
    const fetcher = vi.fn(async () => new Response(null, { status: 204 }));
    await new NativeTransport('https://api.test', fetcher).request('/auth/logout', {
      method: 'POST',
      token: 'proof',
    });
    expect(fetcher.mock.calls[0]).toEqual([
      'https://api.test/api/v1/auth/logout',
      expect.objectContaining({
        credentials: 'omit',
        headers: { Accept: 'application/json', Authorization: 'Bearer proof' },
      }),
    ]);
  });
  it('exposes public problem details and rate-limit information', async () => {
    const transport = new NativeTransport('https://api.test', async () =>
      Response.json(
        { code: 'throttled', detail: 'Try later' },
        { status: 429, headers: { 'Retry-After': '30' } },
      ),
    );
    await expect(transport.request('/auth/login')).rejects.toMatchObject({
      status: 429,
      code: 'throttled',
      retryAfter: 30,
    });
  });
  it('propagates caller cancellation', async () => {
    const abort = new AbortController();
    const transport = new NativeTransport('https://api.test', async (_url, init) => {
      expect(init?.signal?.aborted).toBe(true);
      throw new DOMException('Aborted', 'AbortError');
    });
    abort.abort();
    await expect(transport.request('/work-orders', { signal: abort.signal })).rejects.toMatchObject(
      { name: 'AbortError' },
    );
  });
  it('bounds hung requests with a timeout', async () => {
    const transport = new NativeTransport(
      'https://api.test',
      (_url, init) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
        }),
      5,
    );
    await expect(transport.request('/work-orders')).rejects.toMatchObject({
      code: 'request_timeout',
    });
  });
  it('rejects alternate-origin paths and encodes query values', async () => {
    const fetcher = vi.fn();
    await expect(
      new NativeTransport('https://api.test', fetcher).request('//evil.test'),
    ).rejects.toThrow();
    expect(fetcher).not.toHaveBeenCalled();
    expect(queryString({ search: 'a&b', page: 2, empty: undefined })).toBe('search=a%26b&page=2');
  });
});

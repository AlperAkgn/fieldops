import { X509Certificate } from 'node:crypto';
import { isIP } from 'node:net';

export function validationOrigin(value) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error('Set EXPO_PUBLIC_API_BASE_URL to a device-reachable HTTPS origin.');
  }

  const host = url.hostname;
  if (
    url.protocol !== 'https:' ||
    url.username ||
    url.password ||
    url.href !== `${url.origin}/` ||
    host === 'localhost' ||
    host.endsWith('.localhost') ||
    host.startsWith('127.') ||
    host === '0.0.0.0' ||
    host === '10.0.2.2' ||
    (isIP(host) !== 4 && !/^(?:[a-z0-9-]+\.)+[a-z0-9-]+$/.test(host))
  ) {
    throw new Error(
      'Use a phone-reachable HTTPS origin with a DNS name or IPv4 address and no path or credentials.',
    );
  }
  return url;
}

export function validationCa(value) {
  if (!value) return undefined;
  const der = Buffer.from(value, 'base64');
  if (der.toString('base64') !== value) {
    throw new Error('FIELDOPS_ANDROID_CA_DER_BASE64 must contain one base64 DER CA certificate.');
  }
  const cert = new X509Certificate(der);
  if (
    !cert.raw.equals(der) ||
    !cert.ca ||
    Date.now() < Date.parse(cert.validFrom) ||
    Date.now() > Date.parse(cert.validTo)
  ) {
    throw new Error(
      'The Android validation trust anchor must be one currently valid CA certificate.',
    );
  }
  return cert;
}

import { get } from 'node:https';
import { validationOrigin, validationCa } from './validation-tls.mjs';

try {
  const origin = validationOrigin(process.env.EXPO_PUBLIC_API_BASE_URL);
  const ca = validationCa(process.env.FIELDOPS_ANDROID_CA_DER_BASE64);

  for (const path of ['/health/live', '/health/ready']) {
    const url = new URL(path, origin);
    const response = await new Promise((resolve, reject) => {
      get(
        url,
        { ca: ca?.toString(), rejectUnauthorized: true, signal: AbortSignal.timeout(10_000) },
        resolve,
      ).on('error', reject);
    });
    if (response.statusCode !== 200) {
      response.destroy();
      throw new Error(`${path} returned HTTP ${response.statusCode}. Expected HTTP 200.`);
    }
    response.setEncoding('utf8');
    let body = '';
    for await (const chunk of response) {
      body += chunk;
      if (body.length > 16_384) throw new Error(`${path} returned an oversized health response.`);
    }
    const report = JSON.parse(body);
    if (
      report?.status !== 'Healthy' ||
      !Array.isArray(report.checks) ||
      (path === '/health/ready' &&
        !report.checks.some(({ name, status }) => name === 'postgresql' && status === 'Healthy'))
    ) {
      throw new Error(`${path} did not report FieldOps health.`);
    }
    console.log(`${path}: Healthy over verified HTTPS.`);
  }
} catch (error) {
  console.error(`Android validation preflight failed: ${error.message}`);
  process.exitCode = 1;
}

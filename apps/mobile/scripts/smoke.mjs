import { spawn, execFile } from 'node:child_process';
import { createRequire } from 'node:module';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { setTimeout as delay } from 'node:timers/promises';

// Exercise the development path as well as exports: typed route generation runs
// only on startup and has a different Node resolution path in a workspace.
const projectRoot = fileURLToPath(new URL('../', import.meta.url));
const require = createRequire(new URL('../package.json', import.meta.url));
const reservation = createServer();
await new Promise((resolve, reject) => {
  reservation.once('error', reject);
  reservation.listen(0, '127.0.0.1', resolve);
});
const port = reservation.address().port;
await new Promise((resolve) => reservation.close(resolve));

const baseUrl = `http://localhost:${port}`;
const child = spawn(process.execPath, [require.resolve('expo/bin/cli'), 'start', '--host', 'localhost', '--port', String(port)], {
  cwd: projectRoot,
  env: { ...process.env, CI: '1', EXPO_NO_TELEMETRY: '1' },
  stdio: ['ignore', 'pipe', 'pipe'],
  windowsHide: true,
});
let output = '';
let spawnError;
child.on('error', (error) => { spawnError = error; });
for (const stream of [child.stdout, child.stderr]) {
  stream.on('data', (chunk) => { output = (output + chunk.toString()).slice(-12_000); });
}

try {
  const deadline = Date.now() + 60_000;
  let running = false;
  while (Date.now() < deadline) {
    if (spawnError) throw spawnError;
    if (child.exitCode !== null || child.signalCode !== null) throw new Error('Expo exited before the startup smoke check completed.');
    try {
      const status = await fetch(`${baseUrl}/status`, { signal: AbortSignal.timeout(1_500) });
      running = status.ok && (await status.text()) === 'packager-status:running';
    } catch { /* The server is still starting. */ }
    if (running) break;
    await delay(250);
  }
  if (!running) throw new Error('Metro did not become available within 60 seconds.');

  const response = await fetch(baseUrl, {
    headers: { Accept: 'application/expo+json', 'expo-platform': 'android' },
    signal: AbortSignal.timeout(15_000),
  });
  if (!response.ok) throw new Error(`Native manifest request failed (${response.status}).`);
  const manifest = await response.json();
  if (typeof manifest.launchAsset?.url !== 'string') throw new Error('The Android manifest has no launch bundle.');
  const bundleUrl = new URL(manifest.launchAsset.url);
  const bundle = await fetch(new URL(bundleUrl.pathname + bundleUrl.search, baseUrl), {
    signal: AbortSignal.timeout(90_000),
  });
  if (!bundle.ok || !(bundle.headers.get('content-type') || '').includes('javascript')) {
    throw new Error(`Android development bundle request failed (${bundle.status}).`);
  }
  const bundleSize = (await bundle.arrayBuffer()).byteLength;
  if (bundleSize === 0) throw new Error('Metro returned an empty Android development bundle.');
  process.stdout.write(`Expo startup, Android manifest, and native HTTP bundle passed (${bundleSize} bytes).\n`);
} catch (error) {
  process.stderr.write(output);
  throw error;
} finally {
  if (child.exitCode === null && child.signalCode === null) {
    if (process.platform === 'win32') {
      await promisify(execFile)('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true }).catch(() => {});
    } else {
      child.kill('SIGTERM');
      await Promise.race([new Promise((resolve) => child.once('exit', resolve)), delay(2_000)]);
      if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
    }
  }
}

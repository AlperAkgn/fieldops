import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ command, mode }) => {
  const root = fileURLToPath(new URL('../..', import.meta.url));
  const env = loadEnv(mode, root, 'FIELDOPS_');
  const passphrase = env.FIELDOPS_HTTPS_CERT_PASSWORD;
  const tls = command === 'serve' && mode !== 'test' && passphrase
    ? { https: { pfx: readFileSync(resolve(root, env.FIELDOPS_HTTPS_CERT_PATH || '.certs/fieldops.pfx')), passphrase } }
    : {};

  return {
    plugins: [react(), tailwindcss()],
    server: { port: 5173, strictPort: true, ...tls },
    test: { environment: 'node', include: ['src/**/*.test.ts'] },
  };
});

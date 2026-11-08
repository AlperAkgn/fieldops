import { defineConfig } from '@playwright/test';
const artifacts = process.env.FIELDOPS_E2E_ARTIFACTS || '../../artifacts/phase-3';
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90000,
  expect: { timeout: 10000 },
  use: {
    actionTimeout: 12000,
    timezoneId: 'UTC',
    navigationTimeout: 20000,
    channel: process.env.FIELDOPS_BROWSER_CHANNEL || 'chromium',
    baseURL: process.env.FIELDOPS_WEB_URL || 'https://localhost:5173',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  reporter: [['list'], ['html', { open: 'never', outputFolder: `${artifacts}/playwright-report` }]],
  outputDir: `${artifacts}/playwright-results`,
});

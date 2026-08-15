import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for FashionStore UI-level verification.
 *
 * The storefront is a server-rendered Razor application (Tailwind + vanilla JS),
 * so the specs target real URLs and data-attribute selectors exposed by the
 * server-rendered views. Two viewport projects are defined, one for desktop and
 * one for a representative mobile viewport, as required by Phase 31.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['list']]
    : [['list']],
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  use: {
    baseURL: process.env.FASHIONSTORE_BASE_URL ?? 'http://localhost:5000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium-desktop',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1280, height: 800 },
      },
    },
    {
      name: 'chromium-mobile',
      use: {
        ...devices['Pixel 7'],
        viewport: { width: 390, height: 844 },
        isMobile: true,
        hasTouch: true,
      },
    },
  ],
});

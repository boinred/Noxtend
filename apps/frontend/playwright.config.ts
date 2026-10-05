import { defineConfig, devices } from '@playwright/test'

/**
 * Design Ref: §8.3, §8.4, §8.5 — L2/L3/L4 실행 환경.
 * 기준 해상도는 1440×900 으로 고정한다 (시각 비교의 전제).
 */
export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  timeout: 30_000,
  expect: { timeout: 7_000 },
  use: {
    baseURL: 'http://127.0.0.1:4173',
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium-1440x900',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
  ],
  webServer: {
    command: 'pnpm build && pnpm preview --port 4173 --strictPort --host 127.0.0.1',
    url: 'http://127.0.0.1:4173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})

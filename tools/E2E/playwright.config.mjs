import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: './tests',
    globalSetup: './setup.mjs',
    timeout: 180_000,
    expect: { timeout: 15_000 },
    // A conversion stops when its page loses foreground. Keep expensive media
    // jobs sequential and give each test a fresh browser context and library.
    workers: 1,
    fullyParallel: false,
    forbidOnly: !!process.env.CI,
    retries: 0,
    outputDir: './test-results',
    reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
    use: {
        browserName: 'chromium',
        actionTimeout: 15_000,
        ...(process.env.E2E_CHANNEL ? { channel: process.env.E2E_CHANNEL } : {}),
        locale: 'en-GB',
        serviceWorkers: 'block',
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure'
    },
    projects: [
        { name: 'desktop', use: { viewport: { width: 1280, height: 900 } } },
        { name: 'mobile', use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } }
    ]
});

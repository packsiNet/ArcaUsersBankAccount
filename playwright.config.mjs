import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests', outputDir:'.local/playwright-output', timeout: 30000, workers:1, fullyParallel:false,
  use:{ baseURL: process.env.TEST_BASE_URL ?? 'http://127.0.0.1:5080', browserName:'chromium', headless:true },
  reporter:[['list'],['json',{outputFile:'test-results/results.json'}]],
});

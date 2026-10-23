import { test as base, expect } from '@playwright/test';
import { NO_EDGE_ENV } from './globalSetup';

/** `test` for all specs: skipped when global setup found no Edge (see ./globalSetup). */
export const test = base.extend<{ edgeRequired: void }>({
  edgeRequired: [
    async ({}, use, testInfo) => {
      testInfo.skip(!!process.env[NO_EDGE_ENV], 'Microsoft Edge is required for UI tests but is not installed.');
      await use();
    },
    { auto: true },
  ],
});

export { expect };

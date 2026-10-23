import { chromium } from '@playwright/test';

/** Set for the test workers when Microsoft Edge cannot be launched locally; read by the fixture in ./fixtures. */
export const NO_EDGE_ENV = 'GLANCE_E2E_NO_EDGE';

/**
 * The one Edge check for the whole suite: let Playwright resolve channel 'msedge' by launching it once.
 * On CI a missing Edge fails the run; locally the specs are skipped under a loud banner.
 */
export default async function globalSetup(): Promise<void> {
  try {
    const browser = await chromium.launch({ channel: 'msedge' });
    await browser.close();
  } catch (err) {
    const reason = err instanceof Error ? err.message.split('\n')[0] : String(err);
    if (process.env.CI) {
      throw new Error(`Microsoft Edge is required for the UI tests but could not be launched: ${reason}`);
    }
    process.env[NO_EDGE_ENV] = '1';
    const bar = '!'.repeat(78);
    console.warn(
      `\n${bar}\n!! UI TESTS SKIPPED: Microsoft Edge could not be launched (${reason}).\n!! Install Edge to run them; with CI set this is an error.\n${bar}\n`,
    );
  }
}

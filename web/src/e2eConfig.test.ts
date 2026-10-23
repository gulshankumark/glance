import { describe, expect, it } from 'vitest';
import config from '../playwright.config';

describe('Playwright config', () => {
  it('builds a fresh bundle before serving it to the e2e tests', () => {
    const server = Array.isArray(config.webServer) ? config.webServer[0] : config.webServer;
    expect(server?.command).toMatch(/^npm run build && /);
    expect(server?.reuseExistingServer).toBe(false);
  });
});

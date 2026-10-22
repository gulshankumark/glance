import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import { mockApi, seedDevice } from './mockApi';

test.beforeEach(async () => {
  if (process.platform === 'win32' && !fs.existsSync('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe')) {
    test.skip(true, 'Microsoft Edge is required for UI tests but is not installed.');
  }
});

test.describe('Display Tab', () => {
  test('shows device card with preview and fps', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#display');

    const card = page.locator('[data-testid="device-card-thermalright-hid-5302_SER1"]');
    await expect(card).toBeVisible();

    await expect(card).toContainText('Thermalright 1280x480 LCD');
    await expect(card).toContainText('1280x480');
    await expect(card).toContainText('10.0 fps');

    // Live preview image exists
    const preview = card.locator('img[data-testid="device-preview"]');
    await expect(preview).toBeVisible();
    await expect(preview).toHaveAttribute('src', /\/api\/preview\/thermalright-hid-5302_SER1\.jpg/);
  });

  test('changing layout PUTs activeLayout', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');

    const select = page.locator('select[aria-label="Active Layout"]');
    await expect(select).toBeVisible();

    await Promise.all([
      page.waitForResponse(
        (res) => res.url().includes('/api/devices') && res.request().method() === 'PUT'
      ),
      select.selectOption('Gaming'),
    ]);

    const putCall = api.calls.find(
      (c) => c.method === 'PUT' && c.path === '/api/devices/thermalright-hid-5302_SER1'
    );
    expect(putCall).toBeDefined();
    expect(putCall?.body).toMatchObject({ activeLayout: 'Gaming' });
  });

  test('Flip 180° PUTs rotation', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');

    const flipBtn = page.locator('button:has-text("Flip 180°")');
    await expect(flipBtn).toBeVisible();

    await Promise.all([
      page.waitForResponse(
        (res) => res.url().includes('/api/devices') && res.request().method() === 'PUT'
      ),
      flipBtn.click(),
    ]);

    const putCall = api.calls.find(
      (c) => c.method === 'PUT' && c.path === '/api/devices/thermalright-hid-5302_SER1'
    );
    expect(putCall).toBeDefined();
    expect(putCall?.body).toMatchObject({ rotation: 180 });
  });

  test('preview refresh pauses while hidden', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#display');

    // Initial load requested preview
    const preview = page.locator('img[data-testid="device-preview"]');
    await expect(preview).toBeVisible();

    // Hide page
    await page.evaluate(() => {
      Object.defineProperty(document, 'visibilityState', {
        value: 'hidden',
        writable: true,
      });
      document.dispatchEvent(new Event('visibilitychange'));
    });

    const srcBefore = await preview.getAttribute('src');

    // Wait 1.5s
    await page.waitForTimeout(1500);

    const srcAfterHidden = await preview.getAttribute('src');
    expect(srcAfterHidden).toBe(srcBefore);

    // Unhide page
    await page.evaluate(() => {
      Object.defineProperty(document, 'visibilityState', {
        value: 'visible',
        writable: true,
      });
      document.dispatchEvent(new Event('visibilitychange'));
    });

    // Wait 1.5s for refresh
    await page.waitForTimeout(1500);
    const srcAfterVisible = await preview.getAttribute('src');
    expect(srcAfterVisible).not.toBe(srcBefore);
  });

  test('empty state without devices', async ({ page }) => {
    await mockApi(page, { devices: [] });
    await page.goto('/#display');

    await expect(page.locator('main')).toContainText(
      'No screen found — plug in a supported display'
    );
  });

  test('first-run call to action without active layout', async ({ page }) => {
    await mockApi(page, {
      devices: [seedDevice({ activeLayout: null, fps: 0, framesSent: 0 })],
    });
    await page.goto('/#display');

    const cta = page.locator('a, button').filter({ hasText: /Choose a layout|Create a layout/ });
    await expect(cta.first()).toBeVisible();

    await cta.first().click();
    expect(page.url()).toContain('#layouts');
  });
});

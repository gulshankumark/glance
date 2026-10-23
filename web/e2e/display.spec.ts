import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi, seedDevice } from './mockApi';

const card = (page: Page) => page.getByTestId('device-card-thermalright-hid-5302_SER1');
const preview = (page: Page) => card(page).getByTestId('device-preview');

async function setVisibility(page: Page, state: 'hidden' | 'visible') {
  await page.evaluate((s) => {
    Object.defineProperty(document, 'visibilityState', { value: s, configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));
  }, state);
}

/** True once the preview <img> shows a decoded image. */
const previewLoaded = (page: Page) =>
  preview(page)
    .evaluate((img) => (img as HTMLImageElement).complete && (img as HTMLImageElement).naturalWidth > 0)
    .catch(() => false);

test.describe('Display Tab', () => {
  test('shows device card with preview and fps', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');

    await expect(card(page)).toBeVisible();
    await expect(card(page)).toContainText('Thermalright 1280x480 LCD');
    await expect(card(page)).toContainText('1280×480');
    await expect(card(page)).toContainText('10.0 fps');

    await expect(preview(page)).toBeVisible();
    await expect.poll(() => previewLoaded(page)).toBe(true);
    expect(api.calls.some((c) => c.path === '/api/preview/thermalright-hid-5302_SER1.jpg')).toBe(true);
  });

  test('changing layout PUTs activeLayout', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');

    const select = page.getByLabel('Active layout');
    await expect(select).toBeVisible();

    await Promise.all([
      page.waitForResponse((res) => res.url().includes('/api/devices') && res.request().method() === 'PUT'),
      select.selectOption('Gaming'),
    ]);

    const putCall = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/devices/thermalright-hid-5302_SER1');
    expect(putCall?.body).toEqual({ activeLayout: 'Gaming' });
    await expect(select).toHaveValue('Gaming');
  });

  test('a failed rotation PUT shows an alert next to the rotation buttons', async ({ page }) => {
    await mockApi(page, { deviceWriteError: 'Rotation rejected' });
    await page.goto('/#display');

    await page.getByRole('button', { name: '90°', exact: true }).click();
    await expect(card(page).getByRole('alert')).toContainText('Rotation rejected');
    await expect(page.getByRole('group', { name: 'Rotation' }).locator('xpath=..').getByRole('alert')).toBeVisible();
  });

  test('Flip 180° PUTs rotation', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');

    await Promise.all([
      page.waitForResponse((res) => res.url().includes('/api/devices') && res.request().method() === 'PUT'),
      page.getByRole('button', { name: 'Flip 180°' }).click(),
    ]);

    const putCall = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/devices/thermalright-hid-5302_SER1');
    expect(putCall?.body).toEqual({ rotation: 180 });
    await expect(page.getByRole('button', { name: '180°', exact: true })).toHaveAttribute('aria-pressed', 'true');
  });

  test('preview refreshes once a second while visible', async ({ page }) => {
    // A paused fake clock makes the schedule exact: each refresh lands 1000 ms after the last one, never earlier.
    const t0 = new Date('2026-01-01T00:00:00Z').getTime();
    await page.clock.install({ time: t0 });
    await page.clock.pauseAt(t0 + 1000);
    const api = await mockApi(page);
    await page.goto('/#display');
    await expect
      .poll(async () => {
        await page.clock.runFor(250);
        // Only once the <img> exists: previewLoaded would otherwise wait for it while the paused clock holds it back.
        return (await preview(page).count()) > 0 && previewLoaded(page);
      })
      .toBe(true);
    // Line up with the refresh schedule: advance until the next refresh is made.
    const start = api.previewRequests();
    let shown = await preview(page).getAttribute('src');
    for (let i = 0; i < 4 && api.previewRequests() === start; i++) {
      await page.clock.runFor(250);
      await page.waitForTimeout(100);
    }

    for (let i = 0; i < 3; i++) {
      const before = api.previewRequests();
      expect(before).toBeGreaterThan(start);
      await expect.poll(() => preview(page).getAttribute('src')).not.toBe(shown); // the refresh has finished
      shown = await preview(page).getAttribute('src');
      await page.clock.runFor(750);
      await page.waitForTimeout(200);
      expect(api.previewRequests()).toBe(before);
      await page.clock.runFor(250);
      await expect.poll(() => api.previewRequests()).toBe(before + 1);
    }
  });

  test('preview refresh pauses while hidden', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');
    await expect.poll(() => previewLoaded(page)).toBe(true);

    await setVisibility(page, 'hidden');
    // Let a request that was already in flight when the page was hidden finish before counting.
    await page.waitForTimeout(300);
    const whileHidden = api.previewRequests();
    await page.waitForTimeout(2500);
    expect(api.previewRequests()).toBe(whileHidden);

    await setVisibility(page, 'visible');
    await expect.poll(() => api.previewRequests(), { timeout: 1500 }).toBeGreaterThan(whileHidden);
  });

  test('device pushes do not speed up the preview refresh', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');
    await expect.poll(() => previewLoaded(page)).toBe(true);

    // Devices pushed every 200 ms for 3 s: still at most one preview request per second.
    const before = api.previewRequests();
    for (let i = 0; i < 15; i++) {
      await api.sendEvent('devices', [seedDevice({ fps: 9 + (i % 3) * 0.5, framesSent: 1234 + i })]);
      await page.waitForTimeout(200);
    }
    expect(api.previewRequests() - before).toBeLessThanOrEqual(4);
  });

  test('keeps the last preview image on 404', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');
    await expect.poll(() => previewLoaded(page)).toBe(true);
    const shown = await preview(page).getAttribute('src');

    api.options.previewStatus = 404;
    const before = api.previewRequests();
    await expect.poll(() => api.previewRequests(), { timeout: 4000 }).toBeGreaterThanOrEqual(before + 2);

    expect(await preview(page).getAttribute('src')).toBe(shown);
    expect(await previewLoaded(page)).toBe(true);
  });

  test('shows the message of a retrying or unsupported screen', async ({ page }) => {
    await mockApi(page, {
      devices: [
        seedDevice({ status: 'retrying', message: 'the screen stopped answering', nextRetry: '2026-10-22T10:00:05Z', fps: 0 }),
        seedDevice({
          key: 'thermalright-hid-5302_SER2',
          serial: 'SER2',
          status: 'unsupported',
          message: 'firmware 9 is not supported',
          fps: 0,
        }),
      ],
    });
    await page.goto('/#display');

    await expect(card(page)).toContainText('the screen stopped answering');
    await expect(page.getByTestId('device-card-thermalright-hid-5302_SER2')).toContainText('firmware 9 is not supported');
  });

  test('fps is greyed after a reconnect until the next sensors event', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');
    const fps = card(page).getByText('10.0 fps');
    await expect(fps).toBeVisible();

    await api.sendEvent('sensors', { 'cpu/t': 55 });
    await expect(fps).not.toHaveAttribute('aria-busy', 'true');

    await api.closeEvents();
    await expect(fps).toHaveAttribute('aria-busy', 'true');
    await expect(page.getByTestId('reconnecting-banner')).toBeHidden({ timeout: 5000 });
    await expect(fps).toHaveAttribute('aria-busy', 'true');

    await api.sendEvent('sensors', { 'cpu/t': 56 });
    await expect(fps).not.toHaveAttribute('aria-busy', 'true');
  });

  test('empty state without devices', async ({ page }) => {
    await mockApi(page, { devices: [] });
    await page.goto('/#display');

    await expect(page.locator('main')).toContainText('No screen found — plug in a supported display');
  });

  test('"Choose a layout" opens the Layouts tab', async ({ page }) => {
    await mockApi(page, { devices: [seedDevice({ activeLayout: null, fps: 0, framesSent: 0 })] });
    await page.goto('/#display');

    await page.getByRole('button', { name: 'Choose a layout' }).click();
    await expect(page).toHaveURL(/#layouts$/);
  });

  test('"Create a layout" starts a new layout', async ({ page }) => {
    await mockApi(page, { devices: [seedDevice({ activeLayout: null, fps: 0, framesSent: 0 })] });
    await page.goto('/#display');

    await page.getByRole('button', { name: 'Create a layout' }).click();
    await expect(page.getByTestId('layout-title')).toContainText('New layout 1');
    await expect(page).toHaveURL(/#layouts$/);
  });
});

test.describe('Display rotation error', () => {
  test('the alert describes the rotation button group', async ({ page }) => {
    await mockApi(page, { deviceWriteError: 'Rotation rejected' });
    await page.goto('/#display');
    await page.getByRole('button', { name: 'Flip 180°' }).click();
    await expect(page.getByRole('alert')).toHaveText('Rotation rejected');
    await expect(page.getByRole('group', { name: 'Rotation' })).toHaveAccessibleDescription('Rotation rejected');
  });
});

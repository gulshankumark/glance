import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi, seedDevice } from './mockApi';

const KEY = 'thermalright-hid-5302_SER1';
const card = (page: Page, key = KEY) => page.getByTestId(`device-card-${key}`);

const retrying = () =>
  seedDevice({
    key: 'retry-dev',
    name: 'Retrying screen',
    status: 'retrying',
    message: 'Connection lost',
    nextRetry: '2026-10-23T10:15:30Z',
    serial: 'SER2',
  });
const unsupported = () =>
  seedDevice({
    key: 'unsup-dev',
    name: 'Unknown screen',
    status: 'unsupported',
    modelCode: '7/9',
    serial: 'SER3',
    activeLayout: null,
    width: 0,
    height: 0,
  });

test.describe('Devices Tab', () => {
  test('lists connected, retrying and unsupported', async ({ page }) => {
    await mockApi(page, { devices: [seedDevice(), retrying(), unsupported()] });
    await page.goto('/#devices');

    const ok = card(page);
    await expect(ok).toContainText('Thermalright 1280x480 LCD');
    await expect(ok).toContainText('thermalright-hid-5302');
    await expect(ok).toContainText('5302');
    await expect(ok).toContainText('SER1');
    await expect(ok).toContainText('connected');

    const retry = card(page, 'retry-dev');
    await expect(retry).toContainText('retrying');
    await expect(retry).toContainText('Connection lost');
    await expect(retry).toContainText('next try');
    await expect(retry).not.toContainText('Invalid Date');

    const unsup = card(page, 'unsup-dev');
    await expect(unsup).toContainText('Connected — unsupported model (code 7/9)');
    await expect(unsup).toContainText('Support can be added by contributors; see CONTRIBUTING.md in the project repository.');
    await expect(unsup.getByLabel('Active layout')).toHaveCount(0);
  });

  test('an invalid nextRetry never shows Invalid Date', async ({ page }) => {
    await mockApi(page, { devices: [{ ...retrying(), nextRetry: 'garbage' }] });
    await page.goto('/#devices');
    await expect(card(page, 'retry-dev')).toContainText('Connection lost');
    await expect(card(page, 'retry-dev')).not.toContainText('Invalid Date');
    await expect(card(page, 'retry-dev')).not.toContainText('next try');
  });

  test('rotation change PUTs', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#devices');

    await Promise.all([
      page.waitForResponse((res) => res.url().includes('/api/devices/') && res.request().method() === 'PUT'),
      card(page).getByLabel('Rotation').selectOption('270'),
    ]);

    const put = api.calls.find((c) => c.method === 'PUT' && c.path === `/api/devices/${KEY}`);
    expect(put?.body).toEqual({ rotation: 270 });
    await expect(card(page).getByLabel('Rotation')).toHaveValue('270');
  });

  test('layout None clears active layout', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#devices');

    await Promise.all([
      page.waitForResponse((res) => res.url().includes('/api/devices/') && res.request().method() === 'PUT'),
      card(page).getByLabel('Active layout').selectOption(''),
    ]);

    const put = api.calls.find((c) => c.method === 'PUT' && c.path === `/api/devices/${KEY}`);
    expect(put?.body).toEqual({ activeLayout: null });
    await expect(card(page).getByLabel('Active layout')).toHaveValue('');
  });

  test('mismatch warning', async ({ page }) => {
    await mockApi(page, { devices: [seedDevice({ layoutMismatch: true }), seedDevice({ key: 'other', serial: 'S9' })] });
    await page.goto('/#devices');

    await expect(card(page)).toContainText('does not match this screen');
    await expect(card(page, 'other')).not.toContainText('does not match this screen');
  });

  test('a failed PUT is shown inline and the shown state is kept', async ({ page }) => {
    await mockApi(page, { deviceWriteError: 'Layout "Gaming" is gone' });
    await page.goto('/#devices');

    await card(page).getByLabel('Rotation').selectOption('90');
    await expect(card(page).getByRole('alert')).toContainText('Layout "Gaming" is gone');
    await expect(card(page).getByLabel('Rotation')).toHaveValue('0');
    // the alert belongs to the rotation control, not the layout one
    await expect(card(page).getByLabel('Rotation')).toHaveAccessibleDescription('Layout "Gaming" is gone');
    await expect(card(page).getByLabel('Active layout')).not.toHaveAttribute('aria-invalid', 'true');
  });

  test('live pushes update the list and the page fits 360 px', async ({ page }) => {
    const api = await mockApi(page);
    await page.setViewportSize({ width: 360, height: 700 });
    await page.goto('/#devices');
    await expect(card(page)).toBeVisible();

    await api.sendEvent('devices', [seedDevice({ status: 'retrying', message: 'Lost it' })]);
    await expect(card(page)).toContainText('Lost it');

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
});

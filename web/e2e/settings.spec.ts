import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockApi } from './mockApi';

test.beforeEach(async () => {
  if (process.platform === 'win32' && !fs.existsSync('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe')) {
    test.skip(true, 'Microsoft Edge is required for UI tests but is not installed.');
  }
});

const section = (page: Page, name: string) => page.getByRole('region', { name });
const fpsSelect = (page: Page) => page.getByLabel('Frame-rate cap for animations with text or gauges');
const customFps = (page: Page) => page.getByLabel('Custom frame rate (1–60 fps)');

async function saveDisplay(page: Page) {
  await Promise.all([
    page.waitForResponse((res) => res.url().endsWith('/api/settings') && res.request().method() === 'PUT'),
    section(page, 'Display').getByRole('button', { name: 'Save' }).click(),
  ]);
}

test.describe('Settings Tab', () => {
  test('unit and clock save', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#settings');

    const display = section(page, 'Display');
    await expect(display.getByLabel('Temperature unit')).toHaveValue('celsius');
    await expect(display.getByLabel('24-hour clock')).toBeChecked();

    await display.getByLabel('Temperature unit').selectOption('fahrenheit');
    await display.getByLabel('24-hour clock').uncheck();
    await saveDisplay(page);

    const put = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/settings');
    expect(put?.body).toEqual({ unit: 'fahrenheit', clock24h: false, maxOverlayAnimationFps: null });
    await expect(display.getByRole('status')).toHaveText('Saved');

    await page.reload();
    await expect(section(page, 'Display').getByLabel('Temperature unit')).toHaveValue('fahrenheit');
    await expect(section(page, 'Display').getByLabel('24-hour clock')).not.toBeChecked();
  });

  test('fps cap presets/custom/off', async ({ page }) => {
    const api = await mockApi(page, { settings: { maxOverlayAnimationFps: 37 } });
    await page.goto('/#settings');
    const lastBody = () => api.calls.filter((c) => c.method === 'PUT' && c.path === '/api/settings').at(-1)?.body;

    // A stored value that is not a preset shows as Custom.
    await expect(fpsSelect(page)).toHaveValue('custom');
    await expect(customFps(page)).toHaveValue('37');
    await expect(page.getByText('Lower values use less CPU; animation speed stays the same')).toBeVisible();
    const options = await fpsSelect(page).locator('option').allTextContents();
    expect(options).toEqual(['Off', '8 fps', '12 fps', '15 fps', '20 fps', '24 fps', 'Custom']);

    await fpsSelect(page).selectOption('15');
    await expect(customFps(page)).toHaveCount(0);
    await saveDisplay(page);
    expect(lastBody()).toMatchObject({ maxOverlayAnimationFps: 15 });

    await fpsSelect(page).selectOption('custom');
    await customFps(page).fill('61');
    await section(page, 'Display').getByRole('button', { name: 'Save' }).click();
    await expect(section(page, 'Display').getByRole('alert')).toHaveText('Enter a whole number from 1 to 60.');
    expect(lastBody()).toMatchObject({ maxOverlayAnimationFps: 15 });

    await customFps(page).fill('42');
    await saveDisplay(page);
    expect(lastBody()).toMatchObject({ maxOverlayAnimationFps: 42 });
    await expect(section(page, 'Display').getByRole('alert')).toHaveCount(0);

    // Off sends an explicit null, which removes the cap.
    await fpsSelect(page).selectOption('off');
    await saveDisplay(page);
    expect(lastBody()).toEqual({ unit: 'celsius', clock24h: true, maxOverlayAnimationFps: null });
    expect(api.settings.maxOverlayAnimationFps).toBeNull();
  });

  test('display save error shows inline', async ({ page }) => {
    await mockApi(page);
    await page.route('**/api/settings', (route) =>
      route.request().method() === 'PUT'
        ? route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ error: 'unknown unit' }) })
        : route.fallback(),
    );
    await page.goto('/#settings');
    await section(page, 'Display').getByRole('button', { name: 'Save' }).click();
    await expect(section(page, 'Display').getByRole('alert')).toHaveText('Unknown unit');
  });

  test('network toggle disabled without password', async ({ page }) => {
    const api = await mockApi(page, { password: '', networkAccess: false });
    await page.goto('/#settings');

    const access = section(page, 'Access');
    const toggle = access.getByLabel('Allow access from other devices on the network');
    await expect(toggle).toBeDisabled();
    await expect(access.getByText('Set a password first to allow network access.')).toBeVisible();

    // No password yet: no current-password field, and setting one enables the toggle.
    await expect(access.getByLabel('Current password')).toHaveCount(0);
    await access.getByLabel('New password', { exact: true }).fill('correct horse');
    await access.getByLabel('Confirm new password').fill('correct horse');
    await access.getByRole('button', { name: 'Set password' }).click();
    await expect(access.getByText('Password saved')).toBeVisible();
    const put = api.calls.find((c) => c.path === '/api/settings/password');
    expect(put?.body).toEqual({ new: 'correct horse' });
    await expect(toggle).toBeEnabled();
  });

  test('password change requires current and confirm', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#settings');
    const access = section(page, 'Access');
    const current = access.getByLabel('Current password');
    const next = access.getByLabel('New password', { exact: true });
    const confirm = access.getByLabel('Confirm new password');
    const submit = access.getByRole('button', { name: 'Change password' });
    const putCount = () => api.calls.filter((c) => c.path === '/api/settings/password').length;

    await expect(current).toHaveAttribute('type', 'password');
    await expect(current).toHaveAttribute('autocomplete', 'current-password');
    await expect(next).toHaveAttribute('autocomplete', 'new-password');
    await expect(confirm).toHaveAttribute('autocomplete', 'new-password');

    await next.fill('newpassword1');
    await confirm.fill('newpassword1');
    await submit.click();
    await expect(access.getByRole('alert')).toHaveText('Enter your current password.');

    await current.fill('password123');
    await confirm.fill('newpassword2');
    await submit.click();
    await expect(access.getByRole('alert')).toHaveText('The passwords do not match.');

    await next.fill('short');
    await confirm.fill('short');
    await submit.click();
    await expect(access.getByRole('alert')).toHaveText('Use at least 8 characters.');
    expect(putCount()).toBe(0);

    await current.fill('wrong-one');
    await next.fill('newpassword1');
    await confirm.fill('newpassword1');
    await submit.click();
    await expect(access.getByRole('alert')).toHaveText('The current password is wrong.');

    api.options.passwordRateLimited = true;
    await submit.click();
    await expect(access.getByRole('alert')).toHaveText('Too many attempts. Wait a minute and try again.');
    api.options.passwordRateLimited = false;

    await current.fill('password123');
    await submit.click();
    await expect(access.getByText('Password saved')).toBeVisible();
    expect(api.calls.filter((c) => c.path === '/api/settings/password').at(-1)?.body).toEqual({
      current: 'password123',
      new: 'newpassword1',
    });
    await expect(current).toHaveValue('');
    await expect(next).toHaveValue('');
    await expect(confirm).toHaveValue('');
  });

  test('port conflict message', async ({ page }) => {
    const api = await mockApi(page, { busyPorts: [9000] });
    await page.goto('/#settings');
    const access = section(page, 'Access');
    await expect(access.getByLabel('Port')).toHaveValue('8735');

    await access.getByLabel('Port').fill('80');
    await access.getByRole('button', { name: 'Save network settings' }).click();
    await expect(access.getByRole('alert')).toHaveText('Use a port from 1024 to 65535.');
    expect(api.calls.some((c) => c.path === '/api/settings/network')).toBe(false);

    await access.getByLabel('Port').fill('9000');
    await access.getByRole('button', { name: 'Save network settings' }).click();
    await expect(access.getByRole('alert')).toHaveText('Port is in use by another program. Choose a different port.');
    await page.waitForTimeout(1700);
    expect(new URL(page.url()).port).toBe('4173');
    await expect(page.getByText('The page will reload at the new address')).toHaveCount(0);
  });

  test('network save reloads at the new address only when it changed', async ({ page }) => {
    const api = await mockApi(page);
    await page.route('http://localhost:9100/**', (route) =>
      route.fulfill({ status: 200, contentType: 'text/html', body: '<title>moved</title>' }),
    );
    await page.goto('/#settings');
    const access = section(page, 'Access');

    // Same port and scheme: saved in place, no reload.
    await access.getByLabel('Allow access from other devices on the network').uncheck();
    await access.getByRole('button', { name: 'Save network settings' }).click();
    await expect(access.getByText('Network settings saved')).toBeVisible();
    expect(api.calls.find((c) => c.path === '/api/settings/network')?.body).toEqual({
      enabled: false,
      https: false,
      port: 8735,
    });
    await page.waitForTimeout(1700);
    expect(new URL(page.url()).port).toBe('4173');

    await access.getByLabel('Port').fill('9100');
    await access.getByRole('button', { name: 'Save network settings' }).click();
    await expect(page.getByText('The page will reload at the new address')).toBeVisible();
    await page.waitForURL('http://localhost:9100/#settings');
  });

  test('https switches the scheme of the new address', async ({ page }) => {
    await mockApi(page);
    let target = '';
    await page.route('https://localhost:8735/**', (route) => {
      target = route.request().url();
      return route.fulfill({ status: 200, contentType: 'text/html', body: '<title>moved</title>' });
    });
    await page.goto('/#settings');
    await section(page, 'Access').getByLabel('Use HTTPS').check();
    await section(page, 'Access').getByRole('button', { name: 'Save network settings' }).click();
    await expect(page.getByText('The page will reload at the new address')).toBeVisible();
    await expect.poll(() => target).toBe('https://localhost:8735/');
  });

  test('downloads logs', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#settings');
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      section(page, 'Logs').getByRole('button', { name: 'Download logs' }).click(),
    ]);
    expect(download.suggestedFilename()).toBe('glance-logs.zip');
  });

  test('shows version', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#settings');
    const about = section(page, 'About');
    await expect(about).toContainText('Version 0.1.0');
    await expect(about).toContainText('GPL-3.0');
    await expect(about.getByRole('link', { name: /project/i })).toHaveAttribute('href', /^https:\/\//);
  });

  test('logout only for remote sessions', async ({ page }) => {
    const api = await mockApi(page, { loopback: false, authenticated: true });
    await page.goto('/#settings');
    await page.getByRole('button', { name: 'Log out' }).click();
    await expect(page).toHaveURL(/#login$/);
    expect(api.calls.some((c) => c.method === 'POST' && c.path === '/api/auth/logout')).toBe(true);
  });

  test('no logout on this computer, fits 360 px', async ({ page }) => {
    await mockApi(page);
    await page.setViewportSize({ width: 360, height: 740 });
    await page.goto('/#settings');
    await expect(section(page, 'About')).toContainText('Version 0.1.0');
    await expect(page.getByRole('button', { name: 'Log out' })).toHaveCount(0);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });
});

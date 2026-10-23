import { test, expect } from './fixtures';
import { mockApi } from './mockApi';

test.describe('App Shell', () => {
  test('tabs switch by hash and highlight active', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#display');

    // Default tab is display
    const displayTab = page.locator('nav a[href="#display"]');
    await expect(displayTab).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('main')).toContainText('Display');

    // Click Layouts
    await page.click('nav a[href="#layouts"]');
    expect(page.url()).toContain('#layouts');
    await expect(page.locator('nav a[href="#layouts"]')).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('main')).toContainText('Layouts');

    // Click Sensors
    await page.click('nav a[href="#sensors"]');
    expect(page.url()).toContain('#sensors');
    await expect(page.locator('nav a[href="#sensors"]')).toHaveAttribute('aria-current', 'page');

    // Click Devices
    await page.click('nav a[href="#devices"]');
    expect(page.url()).toContain('#devices');
    await expect(page.locator('nav a[href="#devices"]')).toHaveAttribute('aria-current', 'page');

    // Click Settings
    await page.click('nav a[href="#settings"]');
    expect(page.url()).toContain('#settings');
    await expect(page.locator('nav a[href="#settings"]')).toHaveAttribute('aria-current', 'page');
  });

  test('401 redirects to login with return', async ({ page }) => {
    await mockApi(page, {
      loopback: false,
      networkAccess: true,
      authenticated: false,
    });

    await page.goto('/#sensors');

    // Trigger an API call that returns 401, or route loads and redirects
    await expect(page).toHaveURL(/#login\?return=%23sensors/);
    await expect(page.locator('h1, h2')).toContainText('Login');
  });

  test('login posts password and returns to hash', async ({ page }) => {
    const api = await mockApi(page, {
      loopback: false,
      networkAccess: true,
      authenticated: false,
      password: 'mypassword',
    });

    await page.goto('/#login?return=%23sensors');

    await page.getByLabel('Password').fill('mypassword');
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/#sensors$/);
    const loginCall = api.calls.find((c) => c.path === '/api/auth/login');
    expect(loginCall).toBeDefined();
    expect(loginCall?.body).toEqual({ password: 'mypassword' });
  });

  test('wrong password shows the message and keeps the return hash', async ({ page }) => {
    await mockApi(page, {
      loopback: false,
      networkAccess: true,
      authenticated: false,
      password: 'mypassword',
    });

    await page.goto('/#login?return=%23sensors');

    await page.getByLabel('Password').fill('nope');
    await page.click('button[type="submit"]');

    await expect(page.getByRole('alert')).toHaveText('wrong password');
    await expect(page).toHaveURL(/#login\?return=%23sensors$/);

    await page.getByLabel('Password').fill('mypassword');
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/#sensors$/);
  });

  test('login ignores a return value that is not an app hash', async ({ page }) => {
    await mockApi(page, { loopback: false, networkAccess: true, authenticated: false, password: 'mypassword' });

    await page.goto('/#login?return=%23login%3Freturn%3D%2523sensors');
    await page.getByLabel('Password').fill('mypassword');
    await page.click('button[type="submit"]');

    await expect(page).toHaveURL(/#display$/);
  });

  test('reconnecting banner shows when events drop and hides after reconnect', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');

    const banner = page.getByTestId('reconnecting-banner');
    await expect(banner).toBeHidden();

    // The old test-only status hook must be gone: dispatching it changes nothing.
    await page.evaluate(() => window.dispatchEvent(new CustomEvent('test-live-status', { detail: 'reconnecting' })));
    await expect(banner).toBeHidden();

    // The stream drops (EventSource error) ...
    await api.closeEvents();
    await expect(banner).toBeVisible();
    await expect(banner).toHaveText('Reconnecting…');

    // ... and the provider reconnects after its 1 s backoff; the mock stream opens again.
    await expect(banner).toBeHidden({ timeout: 5000 });
    expect(await page.evaluate(() => (window as unknown as { __eventSourceCount: number }).__eventSourceCount)).toBe(2);
  });

  test('first connection shows Connecting…, not Reconnecting…', async ({ page }) => {
    await mockApi(page, { eventsFail: true });
    await page.goto('/#display');

    const banner = page.getByTestId('reconnecting-banner');
    await expect(banner).toHaveText('Connecting…');
    // Still connecting after a failed retry.
    await page.waitForTimeout(1500);
    await expect(banner).toHaveText('Connecting…');
  });

  test('login page opens no event stream and shows no banner', async ({ page }) => {
    await mockApi(page, { loopback: false, networkAccess: true, authenticated: false });
    await page.goto('/#login?return=%23sensors');

    await expect(page.getByLabel('Password')).toBeVisible();
    await page.waitForTimeout(300);
    await expect(page.getByTestId('reconnecting-banner')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __eventSourceCount?: number }).__eventSourceCount ?? 0)).toBe(0);
  });

  test('a failing event stream for a logged-out remote caller leads to the login page', async ({ page }) => {
    // The Settings stub makes no API call, so only the event stream can notice the missing login.
    await mockApi(page, { loopback: false, networkAccess: true, authenticated: false, eventsFail: true });
    await page.goto('/#settings');

    await expect(page).toHaveURL(/#login\?return=%23settings$/);
  });
});

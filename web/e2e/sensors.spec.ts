import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi } from './mockApi';
import type { SensorsResponse } from '../src/api/types';

const editAlias = (page: Page, name: string) => page.getByRole('button', { name: `Edit alias of ${name}` });
const aliasInput = (page: Page) => page.getByRole('textbox', { name: 'Alias' });
const row = (page: Page, id: string) => page.getByTestId(`sensor-row-${id}`);

test.describe('Sensors tab', () => {
  test('diagnostics banner', async ({ page }) => {
    const sensors: SensorsResponse = {
      sensors: [],
      roles: {},
      pins: {},
      diagnostics: [{ code: 'driver', message: 'The sensor driver is not loaded <b>bold</b>' }],
    };
    await mockApi(page, { sensors });
    await page.goto('/#sensors');
    const banner = page.getByTestId('diagnostic-driver');
    await expect(banner).toContainText('The sensor driver is not loaded <b>bold</b>');
    await expect(banner).toContainText('See the installation guide');
    await expect(banner.getByRole('button')).toHaveCount(0);
  });

  test('alias edit saves with body id', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#sensors');
    await editAlias(page, 'CPU Package').click();
    await aliasInput(page).fill('  Main CPU ');
    await aliasInput(page).press('Enter');
    await expect(row(page, 'cpu/t')).toContainText('Main CPU');
    await expect(aliasInput(page)).toHaveCount(0);
    const put = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/sensors/alias');
    expect(put?.body).toEqual({ id: 'cpu/t', alias: 'Main CPU' });
  });

  test('clearing alias sends null', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#sensors');
    await editAlias(page, 'CPU Package').click();
    await aliasInput(page).fill('');
    await aliasInput(page).press('Enter');
    await expect(aliasInput(page)).toHaveCount(0);
    const put = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/sensors/alias');
    expect(put?.body).toEqual({ id: 'cpu/t', alias: null });
  });

  test('Escape cancels the edit without a request', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#sensors');
    await editAlias(page, 'CPU Package').click();
    await aliasInput(page).fill('Nope');
    await aliasInput(page).press('Escape');
    await expect(aliasInput(page)).toHaveCount(0);
    await expect(row(page, 'cpu/t')).not.toContainText('Nope');
    expect(api.calls.some((c) => c.method === 'PUT')).toBe(false);
  });

  test('a rejected alias shows the error and keeps the edit', async ({ page }) => {
    await mockApi(page, { sensorWriteError: 'alias must be at most 100 characters' });
    await page.goto('/#sensors');
    await editAlias(page, 'CPU Package').click();
    await aliasInput(page).fill('x');
    await aliasInput(page).press('Enter');
    await expect(row(page, 'cpu/t').getByRole('alert')).toContainText('alias must be at most 100 characters');
    await expect(aliasInput(page)).toHaveValue('x');
  });

  test('an HTML alias renders as text', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#sensors');
    await editAlias(page, 'CPU Package').click();
    await aliasInput(page).fill('<img src=x onerror=alert(1)>');
    await aliasInput(page).press('Enter');
    await expect(row(page, 'cpu/t')).toContainText('<img src=x onerror=alert(1)>');
    await expect(page.locator('img[src="x"]')).toHaveCount(0);
  });

  test('pin role and reset to automatic', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#sensors');
    const select = page.getByLabel('CPU temperature');
    await expect(page.getByTestId('role-resolved-cpu.temperature')).toContainText('CPU');
    await select.selectOption({ label: 'CPU Die — AMD Ryzen 9 9950X' });
    await expect(page.getByTestId('role-resolved-cpu.temperature')).toContainText('CPU Die');
    const pins = () => api.calls.filter((c) => c.method === 'PUT' && c.path === '/api/roles/cpu.temperature');
    expect(pins()[0].body).toEqual({ sensorId: '/amdcpu/0/temperature/2' });
    await select.selectOption({ label: 'Automatic' });
    await expect.poll(() => pins().length).toBe(2);
    expect(pins()[1].body).toEqual({ sensorId: null });
    await expect(page.getByTestId('role-resolved-cpu.temperature')).toContainText('CPU');
    await expect(page.getByTestId('role-resolved-cpu.temperature')).not.toContainText('CPU Die');
  });

  test('values update live', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#sensors');
    await expect(row(page, 'cpu/t')).toContainText('55 °C');
    await page.evaluate(() => {
      const es = (window as unknown as { __mockEventSource: EventTarget }).__mockEventSource;
      es.dispatchEvent(new MessageEvent('sensors', { data: JSON.stringify({ 'cpu/t': 71.4 }) }));
    });
    await expect(row(page, 'cpu/t')).toContainText('71 °C');
  });

  test('temperatures follow the unit setting, live', async ({ page }) => {
    const api = await mockApi(page, { settings: { unit: 'fahrenheit' } });
    await page.goto('/#sensors');
    await expect(row(page, 'cpu/t')).toContainText('131 °F');

    // A settings change is announced by a devices event; the page follows without a reload.
    api.settings.unit = 'celsius';
    await api.sendEvent('devices', api.devices);
    await expect(row(page, 'cpu/t')).toContainText('55 °C');
  });

  test('a unit saved in Settings applies to the Sensors tab at once', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#settings');
    await page.getByLabel('Temperature unit').selectOption('fahrenheit');
    await page.getByRole('button', { name: 'Save' }).first().click();
    await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
    await page.goto('/#sensors');
    await expect(row(page, 'cpu/t')).toContainText('131 °F');
  });

  test('search filters the table', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#sensors');
    await page.getByRole('searchbox', { name: 'Search sensors' }).fill('tctl');
    await expect(row(page, '/amdcpu/0/temperature/2')).toBeVisible();
    await expect(row(page, 'cpu/t')).toHaveCount(0);
    await expect(row(page, 'gpu/t')).toHaveCount(0);
  });

  test('fits a 360 px screen without page-level horizontal scroll', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await mockApi(page);
    await page.goto('/#sensors');
    await expect(row(page, 'cpu/t')).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });

  test('a role pinned earlier shows its sensor selected, not Automatic', async ({ page }) => {
    const sensors: SensorsResponse = {
      sensors: [
        { id: 'a', hardware: 'H', hardwareKind: 'cpu', name: 'Alpha', kind: 'load', unit: '%', value: 1, alias: null },
        { id: 'b', hardware: 'H', hardwareKind: 'cpu', name: 'Beta', kind: 'load', unit: '%', value: 2, alias: null },
      ],
      roles: { 'cpu.load': 'b' },
      pins: { 'cpu.load': 'b' },
      diagnostics: [],
    };
    await mockApi(page, { sensors });
    await page.goto('/#sensors');
    await expect(page.getByLabel('CPU load')).toHaveValue('b');
  });

  test('a live sensors event does not clobber the alias being typed', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#sensors');
    await editAlias(page, 'CPU Package').click();
    await aliasInput(page).fill('half typed');
    await page.evaluate(() => {
      const es = (window as unknown as { __mockEventSource: EventTarget }).__mockEventSource;
      es.dispatchEvent(new MessageEvent('sensors', { data: JSON.stringify({ 'cpu/t': 72 }) }));
    });
    await expect(row(page, 'cpu/t')).toContainText('72');
    await expect(aliasInput(page)).toHaveValue('half typed');
  });

  test('the edit button name contains its visible text and focus returns after Save and Escape', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#sensors');
    // label-in-name: the visible alias is part of the accessible name
    await expect(page.getByRole('button', { name: 'CPU Die', exact: false }).first()).toBeVisible();
    const button = editAlias(page, 'CPU Package');
    await expect(button).toHaveText('CPU');
    await expect(page.getByRole('button', { name: /^CPU .*edit alias of CPU Package$/ })).toHaveCount(1);
    await button.click();
    await aliasInput(page).press('Escape');
    await expect(button).toBeFocused();
    await button.click();
    await aliasInput(page).fill('Saved');
    await aliasInput(page).press('Enter');
    await expect(editAlias(page, 'CPU Package')).toBeFocused();
  });

  test('the active tab link is scrolled into view in the nav at 360 px', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await mockApi(page);
    await page.goto('/#settings');
    const inView = await page.evaluate(() => {
      const nav = document.querySelector('nav') as HTMLElement;
      const link = nav.querySelector('[aria-current="page"]') as HTMLElement;
      const n = nav.getBoundingClientRect();
      const l = link.getBoundingClientRect();
      return l.left >= n.left - 1 && l.right <= n.right + 1;
    });
    expect(inView).toBe(true);
  });
});

test.describe('Sensors diagnostics', () => {
  test('each diagnostic is a status message, also with repeated codes', async ({ page }) => {
    await mockApi(page, {
      sensors: {
        sensors: [],
        roles: {},
        pins: {},
        diagnostics: [
          { code: 'driver', message: 'First problem' },
          { code: 'driver', message: 'Second problem' },
        ],
      },
    });
    await page.goto('/#sensors');
    await expect(page.getByRole('status').filter({ hasText: 'First problem' })).toBeVisible();
    await expect(page.getByRole('status').filter({ hasText: 'Second problem' })).toBeVisible();
  });
});

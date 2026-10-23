import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockApi } from './mockApi';
import type { SensorsResponse } from '../src/api/types';

test.beforeEach(async () => {
  if (process.platform === 'win32' && !fs.existsSync('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe')) {
    test.skip(true, 'Microsoft Edge is required for UI tests but is not installed.');
  }
});

const editAlias = (page: Page, name: string) => page.getByRole('button', { name: `Edit alias of ${name}` });
const aliasInput = (page: Page) => page.getByRole('textbox', { name: 'Alias' });
const row = (page: Page, id: string) => page.getByTestId(`sensor-row-${id}`);

test.describe('Sensors tab', () => {
  test('diagnostics banner', async ({ page }) => {
    const sensors: SensorsResponse = {
      sensors: [],
      roles: {},
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
});

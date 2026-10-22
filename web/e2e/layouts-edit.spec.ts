import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockApi, type MockApiHandle } from './mockApi';
import type { Item, Layout, SensorsResponse } from '../src/api/types';

test.beforeEach(async () => {
  if (process.platform === 'win32' && !fs.existsSync('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe')) {
    test.skip(true, 'Microsoft Edge is required for UI tests but is not installed.');
  }
});

const canvas = (page: Page) => page.getByTestId('layout-canvas');
const tree = (page: Page) => page.getByTestId('sensor-tree');
const leaf = (page: Page, ref: string) => page.getByTestId(`sensor-leaf-${ref}`);
const unsaved = (page: Page) => page.getByTestId('unsaved-marker');
const lastRender = (api: MockApiHandle): Layout | undefined => api.renders().at(-1);
const newItems = (api: MockApiHandle, before: number): Item[] => lastRender(api)?.items.slice(before) ?? [];

async function openLayout(page: Page, name: string) {
  await page.goto('/#layouts');
  await page.getByRole('button', { name, exact: true }).click();
  await expect(page.getByTestId('layout-title')).toContainText(name);
}

/** Layout pixels per CSS pixel of the canvas (layouts here are 1280 wide). */
async function layoutScale(page: Page): Promise<number> {
  const b = (await canvas(page).boundingBox())!;
  return 1280 / b.width;
}

const htmlAlias = '<b>Hot</b><img src=x>';
const htmlSensors: SensorsResponse = {
  sensors: [
    { id: 'cpu/t', hardware: 'Ryzen <i>X</i>', hardwareKind: 'cpu', name: 'CPU Package', kind: 'temperature', unit: '°C', value: 55, alias: htmlAlias },
  ],
  roles: { 'cpu.temperature': 'cpu/t' },
  diagnostics: [],
};

test.describe('Layouts editing: sensor tree', () => {
  test('dragging sensor adds bound item at drop point', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    const scale = await layoutScale(page);

    await leaf(page, 'gpu/t').dragTo(canvas(page), { targetPosition: { x: 300 / scale, y: 100 / scale } });

    await expect.poll(() => newItems(api, 3).length).toBe(1);
    const item = newItems(api, 3)[0];
    expect(item).toMatchObject({ type: 'sensor', sensor: 'gpu/t', label: 'GPU' });
    expect(Math.abs(item.x - 300)).toBeLessThanOrEqual(3);
    expect(Math.abs(item.y - 100)).toBeLessThanOrEqual(3);
    await expect(page.getByTestId(`item-box-${item.id}`)).toHaveAttribute('aria-pressed', 'true');
    await expect(unsaved(page)).toBeVisible();
  });

  test('dragging role adds role binding', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await leaf(page, 'role:cpu.temperature').dragTo(canvas(page), { targetPosition: { x: 50, y: 50 } });

    await expect.poll(() => newItems(api, 3)[0]).toMatchObject({
      type: 'sensor', sensor: 'role:cpu.temperature', label: 'CPU temperature',
    });
  });

  test('clicking a sensor adds it in the middle (no drag needed)', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await leaf(page, 'cpu/t').click();
    await expect.poll(() => newItems(api, 3)[0]).toMatchObject({ type: 'sensor', sensor: 'cpu/t', label: 'CPU', x: 640, y: 240 });
  });

  test('sensor tree values update live', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await expect(leaf(page, 'gpu/t')).toContainText('48 °C');
    await api.sendEvent('sensors', { 'cpu/t': 70.2, 'gpu/t': 71.4, '/amdcpu/0/temperature/2': 61 });
    await expect(leaf(page, 'gpu/t')).toContainText('71 °C');
    // A role shows the value of the sensor it resolves to.
    await expect(leaf(page, 'role:cpu.temperature')).toContainText('70 °C');
  });

  test('search filters tree', async ({ page }) => {
    await mockApi(page);
    await openLayout(page, 'Default');

    await tree(page).getByLabel('Search sensors').fill('gpu');
    await expect(leaf(page, 'gpu/t')).toBeVisible();
    await expect(leaf(page, 'role:gpu.temperature')).toBeVisible();
    await expect(leaf(page, 'cpu/t')).toHaveCount(0);
    await expect(leaf(page, 'role:cpu.temperature')).toHaveCount(0);

    // Hardware names match too.
    await tree(page).getByLabel('Search sensors').fill('ryzen tctl');
    await expect(leaf(page, '/amdcpu/0/temperature/2')).toBeVisible();
    await expect(leaf(page, 'cpu/t')).toHaveCount(0);
  });

  test('HTML in labels renders as text', async ({ page }) => {
    const api = await mockApi(page, { sensors: htmlSensors });
    await openLayout(page, 'Default');

    await expect(leaf(page, 'cpu/t')).toContainText(htmlAlias);
    await expect(tree(page).locator('b, i, img')).toHaveCount(0);

    await leaf(page, 'cpu/t').dragTo(canvas(page), { targetPosition: { x: 40, y: 40 } });
    await expect.poll(() => (newItems(api, 3)[0] as { label?: string } | undefined)?.label).toBe(htmlAlias);
    await expect(page.locator('main b, main img[src="x"]')).toHaveCount(0);
  });
});

test.describe('Layouts editing: add menu', () => {
  const palette = (page: Page) => page.getByTestId('add-menu');

  async function pickSensor(page: Page, search: string, option: string) {
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Search').fill(search);
    await dialog.getByRole('button', { name: option, exact: true }).click();
    await expect(dialog).toBeHidden();
  }

  test('add menu adds each type', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');
    let count = 1;
    const added = async () => {
      await expect.poll(() => lastRender(api)?.items.length).toBe(++count);
      const item = lastRender(api)!.items.at(-1)!;
      await expect(page.getByTestId(`item-box-${item.id}`)).toHaveAttribute('aria-pressed', 'true');
      return item;
    };

    await palette(page).getByRole('button', { name: '+ Text' }).click();
    expect(await added()).toMatchObject({ type: 'text', x: 640, y: 240 });

    await palette(page).getByRole('button', { name: '+ Clock' }).click();
    expect(await added()).toMatchObject({ type: 'clock', timeZone: 'local' });

    await palette(page).getByRole('button', { name: '+ Sensor' }).click();
    await pickSensor(page, 'arc', 'GPU — Intel Arc Pro B70');
    expect(await added()).toMatchObject({ type: 'sensor', sensor: 'gpu/t', label: 'GPU' });

    const gauges: Array<[string, string]> = [
      ['Bar', 'bar'], ['Segmented bar', 'segmentedBar'], ['Arc gauge', 'arcGauge'],
      ['Dial', 'dial'], ['History graph', 'historyGraph'], ['Sparkline', 'sparkline'],
    ];
    for (const [name, type] of gauges) {
      await palette(page).getByRole('button', { name: '+ Gauge' }).click();
      await palette(page).getByRole('menuitem', { name, exact: true }).click();
      await pickSensor(page, 'cpu temp', 'Role: CPU temperature');
      expect(await added()).toMatchObject({ type, sensor: 'role:cpu.temperature' });
    }

    await palette(page).getByRole('button', { name: '+ Image' }).click();
    const dialog = page.getByRole('dialog');
    // Only image assets can be shown by an image item.
    await expect(dialog.getByRole('button', { name: /Animation/ })).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Image 1, 320×240' }).click();
    expect(await added()).toMatchObject({ type: 'image', assetId: 'img1', width: 320, height: 240, x: 480, y: 120 });
  });

  test('cancelling a picker adds nothing', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');
    await expect.poll(() => api.renders().length).toBeGreaterThan(0);

    await palette(page).getByRole('button', { name: '+ Sensor' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
    await palette(page).getByRole('button', { name: '+ Image' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

    await expect(page.getByRole('dialog')).toBeHidden();
    await expect(unsaved(page)).toBeHidden();
    expect(lastRender(api)?.items).toHaveLength(1);
  });
});

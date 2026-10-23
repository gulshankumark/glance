import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi, type MockApiHandle } from './mockApi';
import type { Item, Layout } from '../src/api/types';

const canvas = (page: Page) => page.getByTestId('layout-canvas');
const onCanvas = (page: Page, name: RegExp) => canvas(page).getByRole('button', { name });
const guides = (page: Page) => page.getByTestId('snap-guide');
const BAR = /^Bar —/;
const CPU = /^Sensor —/;

/** Drawn left edge of the "CPU temperature" readout (fixed bounds below); the bar starts at x 600, y 300, 400×30. */
const CPU_LEFT = 300;

/** Fixed drawn bounds for the title and the CPU readout (whatever their position); the bar's bounds follow its box. */
async function fixedBounds(page: Page) {
  await page.route('**/api/render/bounds', async (route) => {
    const layout = route.request().postDataJSON() as Layout;
    const out: Record<string, { x: number; y: number; width: number; height: number }> = {};
    for (const item of layout.items as Item[]) {
      if (item.id === 'title') out[item.id] = { x: 40, y: 44, width: 150, height: 38 };
      else if (item.id === 'cpu') out[item.id] = { x: CPU_LEFT, y: 204, width: 180, height: 26 };
      else if ('width' in item) out[item.id] = { x: item.x, y: item.y, width: item.width, height: item.height };
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(out) });
  });
}

async function openDefault(page: Page) {
  await page.goto('/#layouts');
  await page.getByRole('button', { name: 'Default', exact: true }).click();
  await expect(onCanvas(page, BAR)).toBeVisible();
  // The CPU box sits at its fixed drawn bounds once the first render (and its bounds) arrived.
  await expect.poll(async () => {
    const c = (await canvas(page).boundingBox())!;
    const b = (await onCanvas(page, CPU).boundingBox())!;
    return Math.round(((b.x - c.x) / c.width) * 1280);
  }).toBeGreaterThan(CPU_LEFT - 20);
}

/**
 * Presses the bar and drags it horizontally so its left edge would land `offset` layout px right of the CPU text's
 * left edge, on whole screen pixels. Returns the unsnapped x the pointer movement alone gives.
 * `during` runs with the button still down.
 */
async function dragBar(page: Page, offset: number, during?: () => Promise<void>): Promise<number> {
  const c = (await canvas(page).boundingBox())!;
  const scale = c.width / 1280;
  const b = (await onCanvas(page, BAR).boundingBox())!;
  const start = { x: Math.round(b.x + b.width / 2), y: Math.round(b.y + b.height / 2) };
  const endX = Math.round(start.x + (CPU_LEFT + offset - 600) * scale);
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(endX, start.y, { steps: 10 });
  await during?.();
  await page.mouse.up();
  return 600 + Math.round((endX - start.x) / scale);
}

async function savedBarX(page: Page, api: MockApiHandle): Promise<number | undefined> {
  await Promise.all([
    page.waitForResponse((r) => r.url().endsWith('/api/layouts/Default') && r.request().method() === 'PUT'),
    page.getByRole('button', { name: 'Save', exact: true }).click(),
  ]);
  const put = api.calls.filter((x) => x.method === 'PUT' && x.path === '/api/layouts/Default').at(-1)!.body as Layout;
  return put.items.find((i) => i.id === 'bar1')?.x;
}

test.describe('Layouts: snapping', () => {
  test('a drag ending 3 px from another item\'s edge snaps to it and shows a guide while dragging', async ({ page }) => {
    const api = await mockApi(page);
    await fixedBounds(page);
    await openDefault(page);

    const raw = await dragBar(page, 3, async () => {
      await expect(guides(page).first()).toBeVisible();
      await expect(guides(page).first()).toHaveAttribute('aria-hidden', 'true');
    });
    expect(raw).not.toBe(CPU_LEFT);
    await expect(guides(page)).toHaveCount(0);
    expect(await savedBarX(page, api)).toBe(CPU_LEFT);
  });

  test('holding Alt disables snapping', async ({ page }) => {
    const api = await mockApi(page);
    await fixedBounds(page);
    await openDefault(page);

    await page.keyboard.down('Alt');
    const raw = await dragBar(page, 3, async () => {
      await expect(guides(page)).toHaveCount(0);
    });
    await page.keyboard.up('Alt');
    expect(raw).not.toBe(CPU_LEFT);
    expect(await savedBarX(page, api)).toBe(raw);
  });

  test('a drag ending 20 px away does not snap', async ({ page }) => {
    const api = await mockApi(page);
    await fixedBounds(page);
    await openDefault(page);

    const raw = await dragBar(page, 20, async () => {
      await expect(guides(page)).toHaveCount(0);
    });
    expect(await savedBarX(page, api)).toBe(raw);
  });
});

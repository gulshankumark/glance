import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi, type MockApiHandle } from './mockApi';
import type { Layout } from '../src/api/types';

const canvas = (page: Page) => page.getByTestId('layout-canvas');
/** Selection boxes on the canvas of the "Default" seed layout, by accessible name. */
const onCanvas = (page: Page, name: RegExp) => canvas(page).getByRole('button', { name });
const inList = (page: Page, name: RegExp) => page.getByRole('list', { name: 'Items' }).getByRole('button', { name });
const TITLE = /^Text — Glance/;
const CPU = /^Sensor —/;
const BAR = /^Bar —/;
const properties = (page: Page) => page.getByRole('region', { name: 'Properties' });
const unsaved = (page: Page) => page.getByTestId('unsaved-marker');
const lastRender = (api: MockApiHandle): Layout | undefined => api.renders().at(-1);
const xOf = (layout: Layout | undefined, id: string) => layout?.items.find((i) => i.id === id)?.x;

async function openDefault(page: Page) {
  await page.goto('/#layouts');
  await page.getByRole('button', { name: 'Default', exact: true }).click();
  await expect(onCanvas(page, TITLE)).toBeVisible();
}

/** Selects the title and the CPU readout on the canvas (click, then Shift+click). */
async function selectTwo(page: Page) {
  await onCanvas(page, TITLE).click();
  await onCanvas(page, CPU).click({ modifiers: ['Shift'] });
  await expect(onCanvas(page, TITLE)).toHaveAttribute('aria-pressed', 'true');
  await expect(onCanvas(page, CPU)).toHaveAttribute('aria-pressed', 'true');
}

test.describe('Layouts: multi-selection', () => {
  test('Shift+click adds, Ctrl+click removes, Properties follows', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);

    await selectTwo(page);
    await expect(onCanvas(page, BAR)).toHaveAttribute('aria-pressed', 'false');
    await expect(properties(page)).toContainText('2 items selected');

    await onCanvas(page, CPU).click({ modifiers: ['ControlOrMeta'] });
    await expect(onCanvas(page, CPU)).toHaveAttribute('aria-pressed', 'false');
    await expect(onCanvas(page, TITLE)).toHaveAttribute('aria-pressed', 'true');
    await expect(properties(page)).not.toContainText('items selected');
    await expect(properties(page).getByRole('textbox').first()).toBeVisible();
  });

  test('Items list: Ctrl+click adds, plain click selects only that one', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);

    await inList(page, TITLE).click();
    await inList(page, BAR).click({ modifiers: ['ControlOrMeta'] });
    await expect(inList(page, TITLE)).toHaveAttribute('aria-pressed', 'true');
    await expect(inList(page, BAR)).toHaveAttribute('aria-pressed', 'true');
    await expect(onCanvas(page, BAR)).toHaveAttribute('aria-pressed', 'true');

    await inList(page, CPU).click();
    await expect(inList(page, CPU)).toHaveAttribute('aria-pressed', 'true');
    await expect(inList(page, TITLE)).toHaveAttribute('aria-pressed', 'false');
    await expect(inList(page, BAR)).toHaveAttribute('aria-pressed', 'false');
  });

  test('dragging one of two selected boxes moves both', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    const scale = (await canvas(page).boundingBox())!.width / 1280;
    const b = (await onCanvas(page, CPU).boundingBox())!;
    const start = { x: b.x + b.width / 2, y: b.y + b.height / 2 };
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    await page.mouse.move(start.x + (20 * scale), start.y, { steps: 4 });
    await page.mouse.move(start.x + (40 * scale), start.y, { steps: 4 });
    await page.mouse.up();
    await expect(onCanvas(page, TITLE)).toHaveAttribute('aria-pressed', 'true');

    await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/layouts/Default') && r.request().method() === 'PUT'),
      page.getByRole('button', { name: 'Save', exact: true }).click(),
    ]);
    const put = api.calls.filter((c) => c.method === 'PUT' && c.path === '/api/layouts/Default').at(-1)!.body as Layout;
    const title = xOf(put, 'title')!;
    expect(Math.abs(title - 80)).toBeLessThanOrEqual(1);
    expect(xOf(put, 'cpu')).toBe(title);
    expect(xOf(put, 'bar1')).toBe(600);
  });

  test('arrows, Delete and Ctrl+D act on the whole selection', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    await page.keyboard.press('ArrowRight');
    await expect.poll(() => [xOf(lastRender(api), 'title'), xOf(lastRender(api), 'cpu')]).toEqual([41, 41]);

    await page.keyboard.press('Control+d');
    await expect.poll(() => lastRender(api)?.items.length).toBe(5);
    const copies = lastRender(api)!.items.slice(3);
    expect(copies.map((i) => [i.type, i.x, i.y])).toEqual([['text', 57, 56], ['sensor', 57, 216]]);
    for (const c of copies) await expect(page.getByTestId(`item-box-${c.id}`)).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByTestId('item-box-title')).toHaveAttribute('aria-pressed', 'false');

    await page.keyboard.press('Delete');
    await expect.poll(() => lastRender(api)?.items.length).toBe(3);
    for (const c of copies) await expect(page.getByTestId(`item-box-${c.id}`)).toHaveCount(0);
    await expect(canvas(page).locator('[aria-pressed="true"]')).toHaveCount(0);
    await expect(properties(page)).toContainText('Select an item');
  });

  test('Ctrl+A on the focused canvas selects all, Escape clears', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);

    await canvas(page).focus();
    await page.keyboard.press('ControlOrMeta+a');
    for (const n of [TITLE, CPU, BAR]) await expect(onCanvas(page, n)).toHaveAttribute('aria-pressed', 'true');
    await expect(properties(page)).toContainText('3 items selected');

    await page.keyboard.press('Escape');
    await expect(canvas(page).locator('[aria-pressed="true"]')).toHaveCount(0);
  });

  test('a group delete is one draft change and one render', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await expect.poll(() => api.renders().length).toBeGreaterThan(0);
    await selectTwo(page);
    await expect(unsaved(page)).toBeHidden();

    // A hidden page makes no live refreshes, so every render from here on comes from a draft change.
    await page.evaluate(() => {
      Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await page.waitForTimeout(1200);
    const before = api.renders().length;
    await page.keyboard.press('Delete');
    await expect(unsaved(page)).toBeVisible();
    await expect.poll(() => api.renders().length).toBeGreaterThan(before);
    await page.waitForTimeout(600);

    // Exactly one render, of the draft with both items gone (never one with only one of them removed).
    const after = api.renders().slice(before);
    expect(after.map((l) => l.items.map((i) => i.id))).toEqual([['bar1']]);
  });
});

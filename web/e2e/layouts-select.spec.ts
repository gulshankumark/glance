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
/** Pressed (selected) boxes on the canvas. */
const pressed = (page: Page) => canvas(page).getByRole('button', { pressed: true });
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
    // Copies share their originals' names and come after them: the second of each pair is the pressed copy.
    await expect(pressed(page)).toHaveCount(2);
    for (const n of [TITLE, CPU]) {
      await expect(onCanvas(page, n).nth(0)).toHaveAttribute('aria-pressed', 'false');
      await expect(onCanvas(page, n).nth(1)).toHaveAttribute('aria-pressed', 'true');
    }

    await page.keyboard.press('Delete');
    await expect.poll(() => lastRender(api)?.items.length).toBe(3);
    for (const n of [TITLE, CPU]) await expect(onCanvas(page, n)).toHaveCount(1);
    await expect(pressed(page)).toHaveCount(0);
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
    await expect(pressed(page)).toHaveCount(0);
  });

  test('a group delete never renders a half-removed draft', async ({ page }) => {
    // That it is one draft change is checked at component level (CanvasGroup.test.tsx).
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);
    await expect(unsaved(page)).toBeHidden();

    await page.keyboard.press('Delete');
    await expect(unsaved(page)).toBeVisible();
    await expect.poll(() => lastRender(api)?.items.map((i) => i.id)).toEqual(['bar1']);
    const halfRemoved = (ids: string) => ids === 'cpu,bar1' || ids === 'title,bar1';
    expect(api.renders().some((l) => halfRemoved(l.items.map((i) => i.id).join()))).toBe(false);
  });

  test('Ctrl/Shift press-and-drag on a selected box deselects it and moves nothing', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    const b = (await onCanvas(page, CPU).boundingBox())!;
    const start = { x: b.x + b.width / 2, y: b.y + b.height / 2 };
    for (const key of ['Control', 'Shift']) {
      if (key === 'Shift') await onCanvas(page, CPU).click({ modifiers: ['Shift'] }); // back in the selection
      await page.keyboard.down(key);
      await page.mouse.move(start.x, start.y);
      await page.mouse.down();
      await page.mouse.move(start.x + 60, start.y + 30, { steps: 5 });
      await page.mouse.up();
      await page.keyboard.up(key);
      await expect(onCanvas(page, CPU)).toHaveAttribute('aria-pressed', 'false');
      await expect(onCanvas(page, TITLE)).toHaveAttribute('aria-pressed', 'true');
    }
    await expect(unsaved(page)).toBeHidden();
    expect(api.renders().every((l) => xOf(l, 'cpu') === 40 && xOf(l, 'title') === 40)).toBe(true);
  });

  test('a plain click on a selected box narrows the selection; a drag keeps the group', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    const b = (await onCanvas(page, CPU).boundingBox())!;
    const start = { x: b.x + b.width / 2, y: b.y + b.height / 2 };
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    await page.mouse.move(start.x + 30, start.y, { steps: 5 });
    await page.mouse.up();
    await expect.poll(() => xOf(lastRender(api), 'title')).toBeGreaterThan(40);
    await expect(pressed(page)).toHaveCount(2);

    await onCanvas(page, CPU).click();
    await expect(pressed(page)).toHaveCount(1);
    await expect(onCanvas(page, CPU)).toHaveAttribute('aria-pressed', 'true');
    await expect(properties(page)).not.toContainText('items selected');
  });

  test('Items list: Delete removes the whole selection when the item is part of it', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    await inList(page, TITLE).focus();
    await page.keyboard.press('Delete');
    await expect.poll(() => lastRender(api)?.items.map((i) => i.id)).toEqual(['bar1']);
    await expect(pressed(page)).toHaveCount(0);
  });

  test('Items list: Delete on an item outside the selection removes only that item', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);
    await inList(page, BAR).focus();
    await page.keyboard.press('Delete');
    await expect.poll(() => lastRender(api)?.items.map((i) => i.id)).toEqual(['title', 'cpu']);
    await expect(pressed(page)).toHaveCount(2);
  });

  test('Items list: Shift+Space and Ctrl+Space toggle, Space alone selects only that item', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);

    await inList(page, TITLE).focus();
    await page.keyboard.press('Space');
    await inList(page, CPU).focus();
    await page.keyboard.press('Shift+Space');
    await expect(pressed(page)).toHaveCount(2);
    await inList(page, BAR).focus();
    await page.keyboard.press('Control+Space');
    await expect(pressed(page)).toHaveCount(3);
    await page.keyboard.press('Control+Space');
    await expect(inList(page, BAR)).toHaveAttribute('aria-pressed', 'false');
    await expect(pressed(page)).toHaveCount(2);

    await page.keyboard.press('Space');
    await expect(pressed(page)).toHaveCount(1);
    await expect(onCanvas(page, BAR)).toHaveAttribute('aria-pressed', 'true');
  });
  test('a press with a little jitter is a click: narrows the selection, moves nothing', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    const jitter = async (name: RegExp) => {
      const b = (await onCanvas(page, name).boundingBox())!;
      const at = { x: b.x + b.width / 2, y: b.y + b.height / 2 };
      await page.mouse.move(at.x, at.y);
      await page.mouse.down();
      await page.mouse.move(at.x + 2, at.y + 1);
      await page.mouse.move(at.x - 1, at.y + 2);
      await page.mouse.up();
    };
    await jitter(CPU);
    await expect(pressed(page)).toHaveCount(1);
    await expect(onCanvas(page, CPU)).toHaveAttribute('aria-pressed', 'true');

    // Same on a single selected item.
    await jitter(CPU);
    await expect(onCanvas(page, CPU)).toHaveAttribute('aria-pressed', 'true');
    await expect(unsaved(page)).toBeHidden();
    expect(api.renders().every((l) => l.items.every((i) => {
      const seed = { title: [40, 40], cpu: [40, 200], bar1: [600, 300] }[i.id];
      return seed !== undefined && i.x === seed[0] && i.y === seed[1];
    }))).toBe(true);
  });

  test('a 10 px drag moves the group and keeps it selected', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    await selectTwo(page);

    const b = (await onCanvas(page, CPU).boundingBox())!;
    const at = { x: b.x + b.width / 2, y: b.y + b.height / 2 };
    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.move(at.x + 10, at.y, { steps: 5 });
    await page.mouse.up();

    const scale = (await canvas(page).boundingBox())!.width / 1280;
    await expect.poll(() => xOf(lastRender(api), 'cpu')).toBeGreaterThan(40);
    const moved = xOf(lastRender(api), 'cpu')!;
    expect(Math.abs(moved - (40 + 10 / scale))).toBeLessThanOrEqual(1);
    expect(xOf(lastRender(api), 'title')).toBe(moved);
    await expect(pressed(page)).toHaveCount(2);
  });

  test('Items list: after Delete, focus moves to the next item, the previous one, then the list', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    const list = page.getByRole('list', { name: 'Items' });

    await inList(page, TITLE).focus();
    await page.keyboard.press('Delete');
    await expect(inList(page, CPU)).toBeFocused();

    await inList(page, BAR).focus();
    await page.keyboard.press('Delete');
    await expect(inList(page, CPU)).toBeFocused();

    await page.keyboard.press('Delete');
    await expect(list).toHaveCount(0);
    await expect(page.getByText('No items yet')).toBeFocused();
  });
});

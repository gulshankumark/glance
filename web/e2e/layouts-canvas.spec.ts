import { test, expect, type Page } from '@playwright/test';
import fs from 'node:fs';
import { mockApi, seedDevice, type MockApiHandle } from './mockApi';
import type { Layout } from '../src/api/types';

test.beforeEach(async () => {
  if (process.platform === 'win32' && !fs.existsSync('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe')) {
    test.skip(true, 'Microsoft Edge is required for UI tests but is not installed.');
  }
});

const canvas = (page: Page) => page.getByTestId('layout-canvas');
const preview = (page: Page) => page.getByTestId('layout-preview');
const box = (page: Page, id: string) => page.getByTestId(`item-box-${id}`);
const unsaved = (page: Page) => page.getByTestId('unsaved-marker');

/** True once the canvas <img> shows a decoded image. */
const previewLoaded = (page: Page) =>
  preview(page)
    .evaluate((img) => (img as HTMLImageElement).complete && (img as HTMLImageElement).naturalWidth > 0)
    .catch(() => false);

const lastRender = (api: MockApiHandle): Layout | undefined => api.renders().at(-1);
const itemOf = (layout: Layout | undefined, id: string) => layout?.items.find((i) => i.id === id);

async function openLayout(page: Page, name: string) {
  await page.goto('/#layouts');
  await page.getByRole('button', { name, exact: true }).click();
  await expect(box(page, 'title').or(box(page, 'clock')).first()).toBeVisible();
}

async function center(page: Page, id: string) {
  const b = await box(page, id).boundingBox();
  if (!b) throw new Error(`no box for ${id}`);
  return { x: b.x + b.width / 2, y: b.y + b.height / 2 };
}

test.describe('Layouts canvas', () => {
  test('selecting layout renders preview', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await expect.poll(() => previewLoaded(page)).toBe(true);
    expect(lastRender(api)?.name).toBe('Default');
    expect(api.calls.some((c) => c.method === 'POST' && c.path === '/api/render/bounds')).toBe(true);
    for (const id of ['title', 'cpu', 'bar1']) await expect(box(page, id)).toBeVisible();
    await expect(unsaved(page)).toBeHidden();
  });

  test('every item keeps a usable hit box, even an empty text', async ({ page }) => {
    await mockApi(page, {
      layoutDocs: [{
        name: 'Empty', width: 1280, height: 480, background: { type: 'color', color: '#000000' },
        items: [{ type: 'text', id: 'blank', x: 100, y: 100, text: '', font: { family: 'Inter', size: 32, weight: 400 }, color: '#FFFFFF', align: 'left' }],
      }],
    });
    await page.goto('/#layouts');
    await page.getByRole('button', { name: 'Empty', exact: true }).click();

    const b = await box(page, 'blank').boundingBox();
    expect(b!.width).toBeGreaterThan(5);
    expect(b!.height).toBeGreaterThan(5);
    await box(page, 'blank').click();
    await expect(box(page, 'blank')).toHaveAttribute('aria-pressed', 'true');
  });

  test('drag moves item and marks unsaved', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await expect.poll(() => previewLoaded(page)).toBe(true);

    const canvasBox = (await canvas(page).boundingBox())!;
    const scale = canvasBox.width / 1280;
    const start = await center(page, 'title');
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    await page.mouse.move(start.x + 50, start.y + 20, { steps: 5 });
    await page.mouse.move(start.x + 100, start.y + 40, { steps: 5 });
    await page.mouse.up();

    await expect(unsaved(page)).toBeVisible();
    await expect(box(page, 'title')).toHaveAttribute('aria-pressed', 'true');
    await expect.poll(() => itemOf(lastRender(api), 'title')?.x).toBeCloseTo(40 + 100 / scale, -1);
    expect(Math.abs(itemOf(lastRender(api), 'title')!.y - (40 + 40 / scale))).toBeLessThanOrEqual(2);
  });

  test('arrow keys nudge', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await box(page, 'title').click();
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Shift+ArrowDown');
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.press('ArrowRight');

    await expect.poll(() => itemOf(lastRender(api), 'title')).toMatchObject({ x: 41, y: 50 });
    await expect(unsaved(page)).toBeVisible();
  });

  test('Delete removes and Ctrl+D duplicates the selected item', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await box(page, 'cpu').click();
    await page.keyboard.press('Delete');
    await expect(box(page, 'cpu')).toHaveCount(0);

    await box(page, 'bar1').click();
    await page.keyboard.press('Control+d');
    await expect.poll(() => lastRender(api)?.items.length).toBe(3);
    const copy = lastRender(api)!.items[2];
    expect(copy).toMatchObject({ type: 'bar', x: 616, y: 316 });
    await expect(box(page, copy.id)).toHaveAttribute('aria-pressed', 'true');
  });

  test('save PUTs and clears unsaved', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await box(page, 'title').click();
    await page.keyboard.press('ArrowRight');
    await expect(unsaved(page)).toBeVisible();

    await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/layouts/Default') && r.request().method() === 'PUT'),
      page.getByRole('button', { name: 'Save', exact: true }).click(),
    ]);
    expect(itemOf(api.layouts.get('Default'), 'title')).toMatchObject({ x: 41, y: 40 });
    await expect(unsaved(page)).toBeHidden();
  });

  test('save errors shown and edits kept', async ({ page }) => {
    const api = await mockApi(page, { layoutSaveErrors: ["item 'title': font size must be 4..512"] });
    await openLayout(page, 'Default');

    await box(page, 'title').click();
    await page.keyboard.press('ArrowRight');
    await page.getByRole('button', { name: 'Save', exact: true }).click();

    await expect(page.getByTestId('save-errors')).toContainText("item 'title': font size must be 4..512");
    await expect(unsaved(page)).toBeVisible();
    expect(itemOf(api.layouts.get('Default'), 'title')?.x).toBe(40);

    // The editor stays usable and the edit is still there.
    await box(page, 'title').click();
    await page.keyboard.press('ArrowRight');
    await expect.poll(() => itemOf(lastRender(api), 'title')?.x).toBe(42);

    api.options.layoutSaveErrors = undefined;
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(unsaved(page)).toBeHidden();
    await expect(page.getByTestId('save-errors')).toBeHidden();
    expect(itemOf(api.layouts.get('Default'), 'title')?.x).toBe(42);
  });

  test('make active PUTs activeLayout', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');

    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/devices/') && r.request().method() === 'PUT'),
      page.getByRole('button', { name: 'Make active' }).click(),
    ]);
    const put = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/devices/thermalright-hid-5302_SER1');
    expect(put?.body).toEqual({ activeLayout: 'Gaming' });
  });

  test('make active asks which screen when there are several', async ({ page }) => {
    const api = await mockApi(page, {
      devices: [seedDevice(), seedDevice({ key: 'thermalright-hid-5302_SER2', serial: 'SER2', name: 'Second screen' })],
    });
    await openLayout(page, 'Gaming');

    await page.getByRole('button', { name: 'Make active' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Screen').selectOption('thermalright-hid-5302_SER2');
    await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/devices/') && r.request().method() === 'PUT'),
      dialog.getByRole('button', { name: 'Make active' }).click(),
    ]);
    const put = api.calls.find((c) => c.method === 'PUT' && c.path === '/api/devices/thermalright-hid-5302_SER2');
    expect(put?.body).toEqual({ activeLayout: 'Gaming' });
    await expect(dialog).toBeHidden();
  });

  test('rapid drags keep ≤1 render in flight', async ({ page }) => {
    const api = await mockApi(page, { renderDelayMs: 600 });
    await openLayout(page, 'Default');
    await expect.poll(() => previewLoaded(page)).toBe(true);

    const start = await center(page, 'bar1');
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    // Moves both faster and slower than the 150 ms debounce, while each render takes 600 ms.
    const before = api.renders().length;
    for (let i = 1; i <= 40; i++) {
      await page.mouse.move(start.x - i * 3, start.y - i);
      await page.waitForTimeout(i % 4 === 0 ? 200 : 30);
    }
    await page.mouse.up();
    expect(api.renders().length - before).toBeGreaterThanOrEqual(5);

    // The latest position wins once the drag has settled.
    const canvasBox = (await canvas(page).boundingBox())!;
    const scale = canvasBox.width / 1280;
    await expect.poll(() => itemOf(lastRender(api), 'bar1')?.x, { timeout: 3000 }).toBeCloseTo(600 - 120 / scale, -1);
    expect(await api.maxRendersInFlight()).toBe(1);
  });

  test('preview refreshes live values about once a second', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await expect.poll(() => previewLoaded(page)).toBe(true);

    const before = api.renders().length;
    await page.waitForTimeout(3500);
    const made = api.renders().length - before;
    expect(made).toBeGreaterThanOrEqual(3);
    expect(made).toBeLessThanOrEqual(4);
  });

  test('size mismatch warning', async ({ page }) => {
    await mockApi(page, { devices: [seedDevice({ rotation: 90 })] });
    await openLayout(page, 'Default');

    await expect(page.getByTestId('size-mismatch')).toContainText('Made for 1280×480');
    await expect(page.getByTestId('size-mismatch')).toContainText('480×1280');
  });

  test('#layouts/new opens a new unsaved layout sized for the screen', async ({ page }) => {
    const api = await mockApi(page, { devices: [seedDevice({ rotation: 270 })] });
    await page.goto('/#layouts/new');

    await expect(page.getByTestId('layout-title')).toContainText('New layout 1');
    await expect(page.getByTestId('layout-title')).toContainText('480×1280');
    await expect(unsaved(page)).toBeVisible();
    await expect(page.getByTestId('size-mismatch')).toHaveCount(0);

    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(unsaved(page)).toBeHidden();
    expect(api.layouts.get('New layout 1')).toMatchObject({ width: 480, height: 1280, items: [] });
    await expect(page.getByRole('button', { name: 'New layout 1', exact: true })).toBeVisible();
  });

  test('new layout without a screen is 1280×480', async ({ page }) => {
    await mockApi(page, { devices: [] });
    await page.goto('/#layouts');
    await page.getByRole('button', { name: '+ New' }).click();

    await expect(page.getByTestId('layout-title')).toContainText('New layout 1');
    await expect(page.getByTestId('layout-title')).toContainText('1280×480');
  });

  test('duplicate, rename and delete layouts', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');

    await page.getByRole('button', { name: 'Duplicate' }).click();
    await page.getByRole('dialog').getByLabel('Name').fill('Gaming copy');
    await page.getByRole('dialog').getByRole('button', { name: 'Duplicate' }).click();
    await expect(page.getByRole('button', { name: 'Gaming copy', exact: true })).toBeVisible();
    expect(api.layouts.has('Gaming copy')).toBe(true);

    await page.getByRole('button', { name: 'Gaming copy', exact: true }).click();
    await page.getByRole('button', { name: 'Rename' }).click();
    await page.getByRole('dialog').getByLabel('Name').fill('Racing');
    await page.getByRole('dialog').getByRole('button', { name: 'Rename' }).click();
    await expect(page.getByRole('button', { name: 'Racing', exact: true })).toBeVisible();
    await expect(page.getByTestId('layout-title')).toContainText('Racing');
    expect([...api.layouts.keys()].sort()).toEqual(['Default', 'Gaming', 'Racing']);

    await page.getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByRole('button', { name: 'Racing', exact: true })).toHaveCount(0);
    expect(api.layouts.has('Racing')).toBe(false);
  });

  test('duplicate shows the server error for a taken name', async ({ page }) => {
    await mockApi(page);
    await openLayout(page, 'Gaming');

    await page.getByRole('button', { name: 'Duplicate' }).click();
    await page.getByRole('dialog').getByLabel('Name').fill('Default');
    await page.getByRole('dialog').getByRole('button', { name: 'Duplicate' }).click();
    await expect(page.getByRole('dialog')).toContainText('a layout with that name already exists');
  });
});

async function setVisibility(page: Page, state: 'hidden' | 'visible') {
  await page.evaluate((s) => {
    Object.defineProperty(document, 'visibilityState', { value: s, configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));
  }, state);
}

test.describe('Layouts canvas (review fixes)', () => {
  test('slow renders are not cancelled by the live refresh', async ({ page }) => {
    const api = await mockApi(page, { renderDelayMs: 1200 });
    await openLayout(page, 'Default');

    await expect.poll(() => previewLoaded(page), { timeout: 5000 }).toBe(true);
    const shown = await preview(page).getAttribute('src');
    // It keeps updating: a later frame replaces the first one.
    await expect.poll(() => preview(page).getAttribute('src'), { timeout: 5000 }).not.toBe(shown);
    expect(api.renders().length).toBeGreaterThanOrEqual(2);
  });

  test('live refresh stops while hidden and resumes when visible', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await expect.poll(() => previewLoaded(page)).toBe(true);

    await setVisibility(page, 'hidden');
    await page.waitForTimeout(300);
    const whileHidden = api.renders().length;
    await page.waitForTimeout(2500);
    expect(api.renders().length).toBe(whileHidden);

    await setVisibility(page, 'visible');
    await expect.poll(() => api.renders().length, { timeout: 1500 }).toBeGreaterThan(whileHidden);
  });

  test('only the item boxes take over touch gestures', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await mockApi(page);
    await openLayout(page, 'Default');

    const touch = (l: ReturnType<typeof canvas>) => l.evaluate((el) => getComputedStyle(el).touchAction);
    expect(await touch(canvas(page))).not.toBe('none');
    expect(await touch(box(page, 'title'))).toBe('none');
  });

  test('opening layouts quickly: the last click wins', async ({ page }) => {
    await mockApi(page, { layoutGetDelayMs: { Default: 800 } });
    await page.goto('/#layouts');
    await page.getByRole('button', { name: 'Default', exact: true }).click();
    await page.getByRole('button', { name: 'Gaming', exact: true }).click();

    await expect(page.getByTestId('layout-title')).toContainText('Gaming');
    await page.waitForTimeout(1200);
    await expect(page.getByTestId('layout-title')).toContainText('Gaming');
  });

  test('a reload after #layouts/new does not start a second draft', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#layouts/new');
    await expect(page.getByTestId('layout-title')).toContainText('New layout 1');
    await expect(page).toHaveURL(/#layouts$/);

    await page.reload();
    await expect(page.getByRole('button', { name: 'Default', exact: true })).toBeVisible();
    await expect(page.getByTestId('layout-title')).toHaveCount(0);
  });

  test('the new layout name uses the current layout list', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#layouts');
    await expect(page.getByRole('button', { name: 'Default', exact: true })).toBeVisible();

    // Another browser saved "New layout 1" since this tab loaded its list.
    api.layouts.set('New layout 1', { ...api.layouts.get('Gaming')!, name: 'New layout 1' });
    await page.evaluate(() => {
      window.location.hash = '#layouts/new';
    });
    await expect(page.getByTestId('layout-title')).toContainText('New layout 2');

    api.layouts.set('New layout 2', { ...api.layouts.get('Gaming')!, name: 'New layout 2' });
    await page.getByRole('button', { name: '+ New' }).click();
    // The open new layout was never saved, so starting another asks first (Task 6 unsaved guard).
    await page.getByRole('dialog', { name: 'Unsaved changes' }).getByRole('button', { name: 'Discard' }).click();
    await expect(page.getByTestId('layout-title')).toContainText('New layout 3');
  });

  test('a new layout takes the size of the first connected screen', async ({ page }) => {
    await mockApi(page, {
      devices: [
        seedDevice({ key: 'x_BAD', serial: 'BAD', status: 'unsupported', width: 0, height: 0 }),
        seedDevice({ key: 'x_RETRY', serial: 'RETRY', status: 'retrying', width: 320, height: 240 }),
        seedDevice({ rotation: 90 }),
      ],
    });
    await page.goto('/#layouts/new');
    await expect(page.getByTestId('layout-title')).toContainText('480×1280');
  });

  test('a new layout without a connected screen is 1280×480', async ({ page }) => {
    await mockApi(page, { devices: [seedDevice({ status: 'unsupported', width: 0, height: 0 })] });
    await page.goto('/#layouts/new');
    await expect(page.getByTestId('layout-title')).toContainText('1280×480');
  });
});

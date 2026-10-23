import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi, type MockApiHandle } from './mockApi';
import type { Item, Layout } from '../src/api/types';

const canvas = (page: Page) => page.getByTestId('layout-canvas');
const onCanvas = (page: Page, name: RegExp) => canvas(page).getByRole('button', { name });
const toolbar = (page: Page) => page.getByRole('toolbar', { name: 'Align' });
/** The toolbar element whether or not it is visible (getByRole skips hidden ones). */
const barInDom = (page: Page) => page.locator('[role="toolbar"][aria-label="Align"]');
const button = (page: Page, name: string) => toolbar(page).getByRole('button', { name, exact: true });
const TITLE = /^Text — Glance/;
const CPU = /^Sensor —/;
const BAR = /^Bar —/;

/** Ink offsets from each item's position, so drawn edges differ from the saved x/y (the bar draws its box). */
const INK: Record<string, { dx: number; dy: number; width: number; height: number }> = {
  title: { dx: 3, dy: 4, width: 150, height: 38 },
  cpu: { dx: 7, dy: 2, width: 180, height: 26 },
};

type Box = { x: number; y: number; width: number; height: number };

function drawn(item: Item): Box {
  const ink = INK[item.id];
  if (ink) return { x: item.x + ink.dx, y: item.y + ink.dy, width: ink.width, height: ink.height };
  return 'width' in item ? { x: item.x, y: item.y, width: item.width, height: item.height } : { x: item.x, y: item.y, width: 0, height: 0 };
}

async function inkBounds(page: Page) {
  await page.route('**/api/render/bounds', async (route) => {
    const layout = route.request().postDataJSON() as Layout;
    const out = Object.fromEntries((layout.items as Item[]).map((i) => [i.id, drawn(i)]));
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(out) });
  });
}

/** Opens "Default" and waits until its first bounds arrived (the CPU box then follows its ink, not its declared spot). */
async function openDefault(page: Page) {
  await page.goto('/#layouts');
  const bounds = page.waitForResponse((r) => r.url().endsWith('/api/render/bounds'));
  await page.getByRole('button', { name: 'Default', exact: true }).click();
  await bounds;
  await expect(onCanvas(page, BAR)).toBeVisible();
}

async function selectAll3(page: Page) {
  await onCanvas(page, TITLE).click();
  await onCanvas(page, CPU).click({ modifiers: ['Shift'] });
  await onCanvas(page, BAR).click({ modifiers: ['Shift'] });
  await expect(canvas(page).getByRole('button', { pressed: true })).toHaveCount(3);
}

async function saved(page: Page, api: MockApiHandle): Promise<Item[]> {
  await Promise.all([
    page.waitForResponse((r) => r.url().endsWith('/api/layouts/Default') && r.request().method() === 'PUT'),
    page.getByRole('button', { name: 'Save', exact: true }).click(),
  ]);
  return (api.calls.filter((x) => x.method === 'PUT' && x.path === '/api/layouts/Default').at(-1)!.body as Layout)
    .items as Item[];
}

test.describe('Layouts: align and distribute', () => {
  test('Align left lines up the drawn left edges of the selected items', async ({ page }) => {
    const api = await mockApi(page);
    await inkBounds(page);
    await openDefault(page);
    await expect(barInDom(page)).toHaveCount(0);
    await onCanvas(page, TITLE).click();
    await expect(barInDom(page)).toHaveCount(0);

    await selectAll3(page);
    await button(page, 'Align left').click();

    const items = await saved(page, api);
    const lefts = items.map((i) => drawn(i).x);
    expect(new Set(lefts).size).toBe(1);
    expect(lefts[0]).toBe(43); // the leftmost drawn edge (title ink at 40 + 3) stays put
  });

  test('Distribute vertically makes the gaps between drawn rectangles equal', async ({ page }) => {
    const api = await mockApi(page);
    await inkBounds(page);
    await openDefault(page);

    await selectAll3(page);
    await button(page, 'Distribute vertically').click();

    const rows = (await saved(page, api)).map(drawn).sort((a, b) => a.y - b.y);
    const gaps = rows.slice(1).map((r, i) => r.y - (rows[i].y + rows[i].height));
    expect(gaps[0]).toBe(gaps[1]);
    expect(rows[0].y).toBe(44);
    expect(rows[2].y).toBe(300);
  });

  test('with two selected the distribute buttons are disabled', async ({ page }) => {
    await mockApi(page);
    await inkBounds(page);
    await openDefault(page);

    await onCanvas(page, TITLE).click();
    await onCanvas(page, CPU).click({ modifiers: ['Shift'] });
    await expect(toolbar(page)).toBeVisible();
    for (const name of ['Align left', 'Align centre', 'Align right', 'Align top', 'Align middle', 'Align bottom']) {
      await expect(button(page, name)).toBeEnabled();
    }
    await expect(button(page, 'Distribute horizontally')).toBeDisabled();
    await expect(button(page, 'Distribute vertically')).toBeDisabled();
  });

  test('the toolbar is one Tab stop after the canvas; arrows, Home and End rove, skipping disabled buttons', async ({ page }) => {
    const api = await mockApi(page);
    await inkBounds(page);
    await openDefault(page);

    await onCanvas(page, TITLE).click();
    await onCanvas(page, CPU).click({ modifiers: ['Shift'] });
    await canvas(page).focus();
    await page.keyboard.press('Tab');
    await expect(button(page, 'Align left')).toBeFocused();
    await expect(toolbar(page).locator('[tabindex="0"]')).toHaveCount(1);
    await page.keyboard.press('ArrowRight');
    await expect(button(page, 'Align centre')).toBeFocused();
    await page.keyboard.press('End');
    await expect(button(page, 'Align bottom')).toBeFocused(); // both distribute buttons are disabled
    await page.keyboard.press('ArrowRight');
    await expect(button(page, 'Align left')).toBeFocused(); // wraps
    await page.keyboard.press('ArrowLeft');
    await expect(button(page, 'Align bottom')).toBeFocused();
    await page.keyboard.press('Home');
    await expect(button(page, 'Align left')).toBeFocused();
    await page.keyboard.press('ArrowRight');
    // Leaving and coming back returns to the last focused button.
    await page.keyboard.press('Shift+Tab');
    await expect(canvas(page)).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(button(page, 'Align centre')).toBeFocused();
    await page.keyboard.press('Home');
    await page.keyboard.press('Enter');

    const items = await saved(page, api);
    const lefts = items.filter((i) => i.id !== 'bar1').map((i) => drawn(i).x);
    expect(lefts).toEqual([43, 43]); // the CPU ink (47) moved onto the title's ink edge
    expect(items.find((i) => i.id === 'bar1')!.x).toBe(600);
  });

  test('with three selected, End reaches Distribute vertically and Enter spaces the rows evenly', async ({ page }) => {
    const api = await mockApi(page);
    await inkBounds(page);
    await openDefault(page);

    await selectAll3(page);
    await canvas(page).focus();
    await page.keyboard.press('Tab');
    await page.keyboard.press('ArrowLeft');
    await expect(button(page, 'Distribute vertically')).toBeFocused(); // wraps backwards
    await page.keyboard.press('Home');
    await page.keyboard.press('End');
    await expect(button(page, 'Distribute vertically')).toBeFocused();
    await page.keyboard.press('Enter');

    const rows = (await saved(page, api)).map(drawn).sort((a, b) => a.y - b.y);
    const gaps = rows.slice(1).map((r, i) => r.y - (rows[i].y + rows[i].height));
    expect(gaps).toEqual([96, 96]);
  });

  test('the canvas does not move when the bar appears; at 360 px no bar is mounted with 0 or 1 selected', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await mockApi(page);
    await inkBounds(page);
    await openDefault(page);

    await expect(barInDom(page)).toHaveCount(0);
    await onCanvas(page, TITLE).click();
    await expect(barInDom(page)).toHaveCount(0);
    const before = (await canvas(page).boundingBox())!.y;
    await onCanvas(page, CPU).click({ modifiers: ['Shift'] });
    await expect(toolbar(page)).toBeVisible();
    expect((await canvas(page).boundingBox())!.y).toBe(before);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  });
});

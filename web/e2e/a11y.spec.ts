import type { Locator, Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi } from './mockApi';

const TABS = ['Display', 'Layouts', 'Sensors', 'Devices', 'Settings'] as const;
const PHONE = { width: 360, height: 740 };

/** Opens a tab; for Layouts the "Default" layout is opened in the editor. */
async function openTab(page: Page, tab: (typeof TABS)[number]) {
  await page.goto(`/#${tab.toLowerCase()}`);
  if (tab === 'Layouts') {
    await page.getByRole('button', { name: 'Default', exact: true }).click();
    await expect(page.getByTestId('layout-canvas')).toBeVisible();
  } else if (tab === 'Sensors') {
    await expect(page.getByRole('searchbox', { name: 'Search sensors' })).toBeVisible();
  } else if (tab === 'Settings') {
    await expect(page.getByRole('button').first()).toBeVisible();
  } else {
    await expect(page.getByText('Thermalright 1280x480 LCD').first()).toBeVisible();
  }
}

/** Presses Tab until `target` has focus (at most `max` times). */
async function tabTo(page: Page, target: Locator, max = 60) {
  for (let i = 0; i < max; i++) {
    if (await target.evaluate((el) => el === document.activeElement)) return;
    await page.keyboard.press('Tab');
  }
  throw new Error('target not reachable with Tab');
}

const INTERACTIVE = /^\s*- (button|link|textbox|searchbox|combobox|checkbox|radio|menuitem|spinbutton|slider|switch|tab)\b(.*)$/;

/** Lines of the page's aria snapshot naming an interactive role without an accessible name. */
async function unnamedControls(page: Page): Promise<string[]> {
  const snapshot = await page.locator('body').ariaSnapshot();
  return snapshot
    .split('\n')
    .filter((line) => {
      const m = INTERACTIVE.exec(line);
      return m !== null && !/^\s*"/.test(m[2]);
    });
}

test.describe('Accessibility', () => {
  test('all tabs reachable by keyboard', async ({ page }) => {
    await mockApi(page);
    await page.goto('/#display');
    await expect(page.getByRole('heading', { name: 'Display' })).toBeVisible();

    for (const tab of TABS) {
      const link = page.getByRole('navigation').getByRole('link', { name: tab, exact: true });
      await tabTo(page, link);
      await page.keyboard.press('Enter');
      await expect(page).toHaveURL(new RegExp(`#${tab.toLowerCase()}$`));
      await expect(link).toHaveAttribute('aria-current', 'page');
    }
  });

  test('controls have accessible names', async ({ page }) => {
    await mockApi(page);
    for (const tab of TABS) {
      await openTab(page, tab);
      expect(await unnamedControls(page), `unnamed controls on ${tab}`).toEqual([]);
    }

    // Role-based lookups of the main controls.
    await openTab(page, 'Display');
    await expect(page.getByRole('combobox', { name: 'Active layout' })).toBeVisible();
    await expect(page.getByRole('group', { name: 'Rotation' }).getByRole('button', { name: 'Flip 180°' })).toBeVisible();
    await openTab(page, 'Layouts');
    for (const name of ['Save', 'Make active', 'Duplicate', 'Rename', 'Delete', '+ Text', '+ Gauge ▸']) {
      await expect(page.getByRole('button', { name, exact: true })).toBeVisible();
    }
    await openTab(page, 'Sensors');
    await expect(page.getByRole('button', { name: /edit alias of CPU Package/ })).toBeVisible();
    await openTab(page, 'Devices');
    await expect(page.getByRole('combobox', { name: 'Rotation' })).toBeVisible();

    await mockApi(page, { loopback: false, networkAccess: true, authenticated: false });
    await page.goto('/#login?return=%23display');
    await expect(page.getByLabel('Password')).toBeVisible();
    expect(await unnamedControls(page), 'unnamed controls on login').toEqual([]);
  });

  test('no horizontal scroll at 360px', async ({ page }) => {
    await page.setViewportSize(PHONE);
    await mockApi(page);
    for (const tab of TABS) {
      await openTab(page, tab);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, `horizontal overflow on ${tab}`).toBeLessThanOrEqual(0);
    }

    // Layouts editor: the panels reflow below the canvas.
    await openTab(page, 'Layouts');
    const canvas = await page.getByTestId('layout-canvas').boundingBox();
    for (const name of ['Sensors', 'Layout items', 'Properties']) {
      const panel = await page.getByRole('region', { name, exact: true }).boundingBox();
      expect(panel!.y, `${name} panel below the canvas`).toBeGreaterThanOrEqual(canvas!.y + canvas!.height);
      expect(panel!.x + panel!.width).toBeLessThanOrEqual(PHONE.width);
    }

    await mockApi(page, { loopback: false, networkAccess: true, authenticated: false });
    await page.goto('/#login?return=%23display');
    await expect(page.getByLabel('Password')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  });

  test('focus visible on keyboard navigation', async ({ page }) => {
    await mockApi(page);
    const outlineOf = (l: Locator) =>
      l.evaluate((el) => {
        const s = getComputedStyle(el);
        return { style: s.outlineStyle, width: parseFloat(s.outlineWidth) };
      });
    const expectRing = async (l: Locator, what: string) => {
      await tabTo(page, l);
      const o = await outlineOf(l);
      expect(o.style, `${what} focus outline style`).not.toBe('none');
      expect(o.width, `${what} focus outline width`).toBeGreaterThanOrEqual(2);
    };

    await page.goto('/#display');
    await expectRing(page.getByRole('navigation').getByRole('link', { name: 'Display' }), 'nav link');
    await expectRing(page.getByRole('combobox', { name: 'Active layout' }), 'select');
    await expectRing(page.getByRole('button', { name: 'Flip 180°' }), 'button');

    await openTab(page, 'Layouts');
    await expectRing(page.getByTestId('layout-canvas'), 'canvas');

    await openTab(page, 'Sensors');
    await expectRing(page.getByRole('searchbox', { name: 'Search sensors' }), 'search box');
  });
});

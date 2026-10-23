import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi } from './mockApi';

async function openDefault(page: Page) {
  await page.goto('/#layouts');
  await page.getByRole('button', { name: 'Default', exact: true }).click();
  await expect(page.getByTestId('layout-canvas')).toBeVisible();
}

test.describe('Gauge menu', () => {
  test('arrow keys, Home/End and Escape; focus returns to the button', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    const button = page.getByRole('button', { name: '+ Gauge ▸' });
    await button.focus();
    await page.keyboard.press('Enter');

    const menu = page.getByRole('menu', { name: 'Gauge type' });
    const items = menu.getByRole('menuitem');
    await expect(button).toHaveAttribute('aria-expanded', 'true');
    await expect(items.first()).toBeFocused();
    await page.keyboard.press('ArrowDown');
    await expect(items.nth(1)).toBeFocused();
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('ArrowUp');
    await expect(items.last()).toBeFocused(); // wraps
    await page.keyboard.press('Home');
    await expect(items.first()).toBeFocused();
    await page.keyboard.press('End');
    await expect(items.last()).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(menu).toHaveCount(0);
    await expect(button).toBeFocused();
    await expect(button).toHaveAttribute('aria-expanded', 'false');
  });

  test('ArrowDown on the button opens the menu', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    const button = page.getByRole('button', { name: '+ Gauge ▸' });
    await button.focus();
    await page.keyboard.press('ArrowDown');
    await expect(page.getByRole('menuitem').first()).toBeFocused();
  });

  test('closes on an outside click', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    await page.getByRole('button', { name: '+ Gauge ▸' }).click();
    await expect(page.getByRole('menu')).toBeVisible();
    await page.getByTestId('layout-title').click();
    await expect(page.getByRole('menu')).toHaveCount(0);
  });

  test('picking a gauge opens the sensor picker; closing it returns focus to the button', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    const button = page.getByRole('button', { name: '+ Gauge ▸' });
    await button.focus();
    await page.keyboard.press('Enter');
    await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(button).toBeFocused();
  });
});

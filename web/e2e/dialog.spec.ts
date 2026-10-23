import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi } from './mockApi';

async function openDefault(page: Page) {
  await page.goto('/#layouts');
  await page.getByRole('button', { name: 'Default', exact: true }).click();
  await expect(page.getByTestId('layout-canvas')).toBeVisible();
}

test.describe('Dialog', () => {
  test('opens with focus inside, traps Tab, Escape cancels and focus returns to the opener', async ({ page }) => {
    const api = await mockApi(page);
    await openDefault(page);
    const opener = page.getByRole('button', { name: 'Delete', exact: true });
    await opener.focus();
    await page.keyboard.press('Enter');

    const dialog = page.getByRole('dialog', { name: 'Delete Default?' });
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveAttribute('aria-labelledby', /.+/);
    await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeFocused();

    // Tab and Shift+Tab never leave the dialog.
    for (let i = 0; i < 5; i++) {
      await page.keyboard.press('Tab');
      expect(await dialog.evaluate((d) => d.contains(document.activeElement))).toBe(true);
    }
    for (let i = 0; i < 5; i++) {
      await page.keyboard.press('Shift+Tab');
      expect(await dialog.evaluate((d) => d.contains(document.activeElement))).toBe(true);
    }

    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(api.calls.some((c) => c.method === 'DELETE')).toBe(false);
  });

  test('deleting the layout a screen shows names that screen', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Delete Default?' })).toContainText(
      'Thermalright 1280x480 LCD (SER1) shows this layout and will show the built-in status screen instead.',
    );
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

    await page.getByRole('button', { name: 'Gaming', exact: true }).click();
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Delete Gaming?' });
    await expect(dialog).toContainText('This cannot be undone.');
    await expect(dialog).not.toContainText('built-in status screen');
  });

  test('a dialog with a text field focuses the field', async ({ page }) => {
    await mockApi(page);
    await openDefault(page);
    await page.getByRole('button', { name: 'Rename', exact: true }).click();
    await expect(page.getByRole('dialog').getByRole('textbox', { name: 'Name' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Rename', exact: true })).toBeFocused();
  });
});

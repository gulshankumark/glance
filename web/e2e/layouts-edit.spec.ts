import type { Page } from '@playwright/test';
import { test, expect } from './fixtures';
import { mockApi, type MockApiHandle } from './mockApi';
import type { Item, Layout, SensorsResponse } from '../src/api/types';

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
  pins: {},
  diagnostics: [],
};

test.describe('Layouts editing: sensor tree', () => {
  // Drag and drop needs the sensor tree and the canvas on screen together (a scroll mid-drag loses the drop).
  test.use({ viewport: { width: 1280, height: 900 } });

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

test.describe('Layouts editing: properties and items', () => {
  const props = (page: Page) => page.getByTestId('properties');
  const items = (page: Page) => page.getByRole('list', { name: 'Items' });
  const itemOf = (api: MockApiHandle, id: string) =>
    lastRender(api)?.items.find((i) => i.id === id) as unknown as Record<string, unknown> | undefined;

  test('property change rerenders', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await page.getByTestId('item-box-bar1').click();

    await props(page).getByLabel('Width', { exact: true }).fill('250');
    await props(page).getByLabel('Width', { exact: true }).press('Enter');
    await expect.poll(() => itemOf(api, 'bar1')?.width).toBe(250);
    await expect(unsaved(page)).toBeVisible();

    // Out-of-range input is clamped to the schema bounds on commit.
    await props(page).getByLabel('Height', { exact: true }).fill('99999');
    await props(page).getByLabel('Height', { exact: true }).blur();
    await expect.poll(() => itemOf(api, 'bar1')?.height).toBe(4096);
    await expect(props(page).getByLabel('Height', { exact: true })).toHaveValue('4096');

    await props(page).getByLabel('Sensor', { exact: true }).selectOption('role:gpu.temperature');
    await expect.poll(() => itemOf(api, 'bar1')?.sensor).toBe('role:gpu.temperature');

    await props(page).getByLabel('Colour', { exact: true }).fill('#80FF0000');
    await props(page).getByLabel('Colour', { exact: true }).press('Enter');
    await expect.poll(() => itemOf(api, 'bar1')?.color).toBe('#80FF0000');

    await props(page).getByLabel('Show value').check();
    await expect.poll(() => itemOf(api, 'bar1')?.showValue).toBe(true);

    await props(page).getByLabel('Font', { exact: true }).selectOption('Bahnschrift');
    await expect.poll(() => (itemOf(api, 'bar1')?.font as { family: string }).family).toBe('Bahnschrift');

    await page.getByTestId('item-box-title').click();
    await expect(props(page).getByLabel('Text', { exact: true })).toHaveValue('Glance');
    await props(page).getByLabel('Text', { exact: true }).fill('Hello');
    await expect.poll(() => itemOf(api, 'title')?.text).toBe('Hello');
  });

  test('clock time zone uses the server list', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');
    await page.getByTestId('item-box-clock').click();
    await props(page).getByLabel('Time zone').selectOption('Asia/Tokyo');
    await expect.poll(() => itemOf(api, 'clock')?.timeZone).toBe('Asia/Tokyo');
  });

  test('thresholds editor add/remove', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await page.getByTestId('item-box-bar1').click();

    await props(page).getByRole('button', { name: '+ Add threshold' }).click();
    await props(page).getByRole('button', { name: '+ Add threshold' }).click();
    await expect.poll(() => itemOf(api, 'bar1')?.thresholds).toEqual([
      { from: 0, color: '#FF4444' },
      { from: 10, color: '#FF4444' },
    ]);

    await props(page).getByLabel('Threshold 2 from').fill('80');
    await props(page).getByLabel('Threshold 2 from').press('Enter');
    await props(page).getByLabel('Threshold 2 colour', { exact: true }).fill('#FFAA00');
    await props(page).getByLabel('Threshold 2 colour', { exact: true }).press('Enter');
    await props(page).getByRole('button', { name: 'Remove threshold 1' }).click();
    await expect.poll(() => itemOf(api, 'bar1')?.thresholds).toEqual([{ from: 80, color: '#FFAA00' }]);
  });

  test('a threshold row moved by Enter keeps its values after blur', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await page.getByTestId('item-box-bar1').click();

    await props(page).getByRole('button', { name: '+ Add threshold' }).click();
    await props(page).getByRole('button', { name: '+ Add threshold' }).click();
    await props(page).getByLabel('Threshold 1 colour', { exact: true }).fill('#00FF00');
    await props(page).getByLabel('Threshold 1 colour', { exact: true }).press('Enter');
    await expect.poll(() => itemOf(api, 'bar1')?.thresholds).toEqual([
      { from: 0, color: '#00FF00' },
      { from: 10, color: '#FF4444' },
    ]);

    // Row 2 moves above row 1 on Enter; leaving its input afterwards must not overwrite the other row.
    const from2 = props(page).getByLabel('Threshold 2 from');
    await from2.fill('-5');
    await from2.press('Enter');
    await expect.poll(() => itemOf(api, 'bar1')?.thresholds).toEqual([
      { from: -5, color: '#FF4444' },
      { from: 0, color: '#00FF00' },
    ]);
    await page.keyboard.press('Tab');
    await page.getByTestId('item-box-bar1').click();
    await page.waitForTimeout(400);
    expect(itemOf(api, 'bar1')?.thresholds).toEqual([
      { from: -5, color: '#FF4444' },
      { from: 0, color: '#00FF00' },
    ]);
    await expect(props(page).getByLabel('Threshold 1 from')).toHaveValue('-5');
    await expect(props(page).getByLabel('Threshold 2 from')).toHaveValue('0');
  });

  test('items list selects and deletes from the keyboard', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');

    await expect(items(page).getByRole('button')).toHaveText(['Text — Glance', 'Sensor — CPU temperature', 'Bar — CPU']);
    const bar = items(page).getByRole('button', { name: 'Bar — CPU' });
    await bar.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('item-box-bar1')).toHaveAttribute('aria-pressed', 'true');
    await expect(bar).toHaveAttribute('aria-pressed', 'true');
    await expect(props(page).getByLabel('Width', { exact: true })).toHaveValue('400');

    await items(page).getByRole('button', { name: 'Text — Glance' }).focus();
    await page.keyboard.press('Space');
    await expect(page.getByTestId('item-box-title')).toHaveAttribute('aria-pressed', 'true');
    await page.keyboard.press('Delete');
    await expect.poll(() => lastRender(api)?.items.map((i) => i.id)).toEqual(['cpu', 'bar1']);
    await expect(page.getByTestId('item-box-title')).toHaveCount(0);
  });

  test('HTML in item names and properties renders as text', async ({ page }) => {
    const font = { family: 'Inter', size: 20, weight: 400 };
    await mockApi(page, {
      sensors: htmlSensors,
      layoutDocs: [{
        name: 'Html', width: 1280, height: 480, background: { type: 'color', color: '#000000' },
        items: [
          { type: 'text', id: 't', x: 1, y: 1, text: '<i>t</i>', font, color: '#FFFFFF', align: 'left' },
          {
            type: 'sensor', id: 's', x: 1, y: 100, sensor: 'cpu/t', label: '<b>L</b>', showUnit: true, decimals: 0,
            font, color: '#FFFFFF', align: 'left',
          },
        ],
      }],
    });
    await page.goto('/#layouts');
    await page.getByRole('button', { name: 'Html', exact: true }).click();

    await expect(items(page).getByRole('button')).toHaveText(['Text — <i>t</i>', `Sensor — ${htmlAlias}`]);
    await page.getByTestId('item-box-s').click();
    await expect(props(page).getByLabel('Label', { exact: true })).toHaveValue('<b>L</b>');
    await expect(page.locator('main b, main i, main img[src="x"]')).toHaveCount(0);
  });
});

test.describe('Layouts editing: background and import', () => {
  const bg = (page: Page) => page.getByTestId('background-picker');

  test('background picker sets colour, image and animation', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');

    await bg(page).getByLabel('Background colour', { exact: true }).fill('#203040');
    await bg(page).getByLabel('Background colour', { exact: true }).press('Enter');
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'color', color: '#203040' });

    await bg(page).getByRole('radio', { name: 'Image' }).check();
    await expect(bg(page).getByRole('button', { name: /Animation/ })).toHaveCount(0);
    await bg(page).getByRole('button', { name: 'Image 1, 320×240' }).click();
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'image', assetId: 'img1' });
    await expect(bg(page).getByRole('button', { name: 'Image 1, 320×240' })).toHaveAttribute('aria-pressed', 'true');

    await bg(page).getByRole('radio', { name: 'Animation' }).check();
    await bg(page).getByRole('button', { name: 'Animation 1, 320×240, 12 frames' }).click();
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'animation', assetId: 'anim1' });

    // Back to a colour keeps the colour chosen before.
    await bg(page).getByRole('radio', { name: 'Colour' }).check();
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'color', color: '#203040' });
  });

  test('a failed upload shows a message and Import works again', async ({ page }) => {
    const api = await mockApi(page, { assetUploadFail: true });
    await openLayout(page, 'Gaming');

    await bg(page).getByRole('radio', { name: 'Image' }).check();
    await bg(page).getByLabel('File to import').setInputFiles({ name: 'a.png', mimeType: 'image/png', buffer: Buffer.from('png') });
    await expect(bg(page).getByRole('alert')).toHaveText('The file could not be uploaded.');
    await expect(bg(page).getByRole('button', { name: 'Import…' })).toBeEnabled();

    api.options.assetUploadFail = undefined;
    await bg(page).getByLabel('File to import').setInputFiles({ name: 'a.png', mimeType: 'image/png', buffer: Buffer.from('png') });
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'image', assetId: 'up2' });
  });

  test('an upload that finishes after a layout switch leaves the new layout alone', async ({ page }) => {
    const api = await mockApi(page, { assetUploadDelayMs: 1500 });
    await openLayout(page, 'Gaming');

    await bg(page).getByRole('radio', { name: 'Animation' }).check();
    await bg(page).getByLabel('File to import').setInputFiles({ name: 't.zt', mimeType: 'application/octet-stream', buffer: Buffer.from('zt') });
    await page.getByRole('dialog').getByRole('button', { name: 'Import' }).click();
    await expect(bg(page).getByRole('status')).toContainText('Uploading t.zt');

    await page.getByRole('button', { name: 'Default', exact: true }).click();
    await expect(page.getByTestId('layout-title')).toContainText('Default');
    await page.waitForTimeout(2200);

    await expect(unsaved(page)).toBeHidden();
    expect(lastRender(api)?.name).toBe('Default');
    expect(lastRender(api)?.background).toEqual({ type: 'color', color: '#101820' });
    expect(api.layouts.get('Gaming')?.background).toEqual({ type: 'color', color: '#000000' });
  });

  test('zt import asks rotation and uploads multipart', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Gaming');

    await bg(page).getByRole('radio', { name: 'Animation' }).check();
    await bg(page).getByLabel('File to import').setInputFiles({
      name: 'theme.zt', mimeType: 'application/octet-stream', buffer: Buffer.from('zt-data'),
    });

    const dialog = page.getByRole('dialog');
    await expect(dialog).toContainText('rotation the file was made for');
    await expect(dialog.getByLabel('Rotation')).toHaveValue('0');
    await dialog.getByLabel('Rotation').selectOption('90');
    await Promise.all([
      page.waitForResponse((r) => r.url().endsWith('/api/assets') && r.request().method() === 'POST'),
      dialog.getByRole('button', { name: 'Import' }).click(),
    ]);

    expect(api.uploads).toHaveLength(1);
    expect(api.uploads[0]).toMatchObject({ fileName: 'theme.zt', kind: 'zt', ztRotation: '90' });
    expect(api.uploads[0].contentType).toMatch(/^multipart\/form-data; boundary=/);
    // The imported animation becomes the background.
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'animation', assetId: 'up1' });
    await expect(bg(page).getByRole('button', { name: 'Animation 2, 320×240, 30 frames' })).toHaveAttribute('aria-pressed', 'true');
  });

  test('image import takes the kind from the extension and shows a too-large error', async ({ page }) => {
    const api = await mockApi(page, { assetUploadStatus: 413 });
    await openLayout(page, 'Gaming');

    await bg(page).getByRole('radio', { name: 'Image' }).check();
    await bg(page).getByLabel('File to import').setInputFiles({
      name: 'photo.JPG', mimeType: 'image/jpeg', buffer: Buffer.from('jpeg'),
    });
    await expect(bg(page).getByRole('alert')).toHaveText('File is too large (max 64 MB).');
    expect(api.uploads[0]).toMatchObject({ fileName: 'photo.JPG', kind: 'image', ztRotation: null });

    api.options.assetUploadStatus = undefined;
    await bg(page).getByLabel('File to import').setInputFiles({
      name: 'photo.png', mimeType: 'image/png', buffer: Buffer.from('png'),
    });
    await expect.poll(() => lastRender(api)?.background).toEqual({ type: 'image', assetId: 'up2' });
    await expect(bg(page).getByRole('alert')).toHaveCount(0);
  });
});

test('the image picker imports an image that can then be picked', async ({ page }) => {
  const api = await mockApi(page);
  await openLayout(page, 'Gaming');

  await page.getByTestId('add-menu').getByRole('button', { name: '+ Image' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('File to import').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: Buffer.from('png') });
  await dialog.getByRole('button', { name: 'Image 2, 320×240' }).click();
  await expect.poll(() => lastRender(api)?.items.at(-1)).toMatchObject({ type: 'image', assetId: 'up1' });
  expect(api.uploads[0]).toMatchObject({ kind: 'image', fileName: 'logo.png' });
});

test.describe('Layouts editing: unsaved changes', () => {
  const guard = (page: Page) => page.getByRole('dialog', { name: 'Unsaved changes' });
  const title = (page: Page) => page.getByTestId('layout-title');
  const titleX = (layout: Layout | undefined) => layout?.items.find((i) => i.id === 'title')?.x;

  async function editDefault(page: Page, api: MockApiHandle, x: number) {
    await page.getByTestId('item-box-title').click();
    await page.keyboard.press('ArrowRight');
    await expect(unsaved(page)).toBeVisible();
    await expect.poll(() => titleX(lastRender(api))).toBe(x);
  }

  test('unsaved prompt on layout switch', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    await editDefault(page, api, 41);

    // Cancel keeps everything.
    await page.getByRole('button', { name: 'Gaming', exact: true }).click();
    await expect(guard(page)).toContainText('Default has unsaved changes');
    await guard(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(guard(page)).toBeHidden();
    await expect(title(page)).toContainText('Default');
    await expect(unsaved(page)).toBeVisible();

    // "+ New" asks too.
    await page.getByRole('button', { name: '+ New' }).click();
    await guard(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(title(page)).toContainText('Default');

    // A failing save keeps the dialog, the errors and the edits.
    api.options.layoutSaveErrors = ["item 'title': bad"];
    await page.getByRole('button', { name: 'Gaming', exact: true }).click();
    await guard(page).getByRole('button', { name: 'Save' }).click();
    await expect(guard(page)).toContainText("item 'title': bad");
    await expect(title(page)).toContainText('Default');
    await expect(unsaved(page)).toBeVisible();

    // Saving then switches.
    api.options.layoutSaveErrors = undefined;
    await guard(page).getByRole('button', { name: 'Save' }).click();
    await expect(title(page)).toContainText('Gaming');
    expect(titleX(api.layouts.get('Default'))).toBe(41);

    // Discard drops the edits and switches.
    await page.getByRole('button', { name: 'Default', exact: true }).click();
    await editDefault(page, api, 42);
    await page.getByRole('button', { name: 'Gaming', exact: true }).click();
    await guard(page).getByRole('button', { name: 'Discard' }).click();
    await expect(title(page)).toContainText('Gaming');
    await expect(unsaved(page)).toBeHidden();
    expect(titleX(api.layouts.get('Default'))).toBe(41);
    expect(await page.evaluate(() => sessionStorage.length)).toBe(0);
  });

  test('unsaved prompt on tab switch and page close', async ({ page }) => {
    const api = await mockApi(page);
    await openLayout(page, 'Default');
    const closing = () =>
      page.evaluate(() => {
        const e = new Event('beforeunload', { cancelable: true });
        window.dispatchEvent(e);
        return e.defaultPrevented;
      });
    expect(await closing()).toBe(false);

    await editDefault(page, api, 41);
    expect(await closing()).toBe(true);

    await page.locator('nav a[href="#display"]').click();
    await expect(guard(page)).toBeVisible();
    await guard(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(page).toHaveURL(/#layouts$/);
    await expect(unsaved(page)).toBeVisible();

    await page.locator('nav a[href="#display"]').click();
    await guard(page).getByRole('button', { name: 'Discard' }).click();
    await expect(page).toHaveURL(/#display$/);
    expect(await closing()).toBe(false);
  });

  test('Back and a typed hash with unsaved edits ask first', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#display');
    await page.locator('nav a[href="#layouts"]').click();
    await page.getByRole('button', { name: 'Default', exact: true }).click();
    await editDefault(page, api, 41);

    // Typed hash: Cancel keeps the editor and the edits.
    await page.evaluate(() => {
      window.location.hash = '#sensors';
    });
    await expect(guard(page)).toBeVisible();
    await expect(page).toHaveURL(/#layouts$/);
    await guard(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(page).toHaveURL(/#layouts$/);
    await expect(unsaved(page)).toBeVisible();

    // Typed hash: Save stores the edits, then goes there.
    await page.evaluate(() => {
      window.location.hash = '#sensors';
    });
    await guard(page).getByRole('button', { name: 'Save' }).click();
    await expect(page).toHaveURL(/#sensors$/);
    expect(titleX(api.layouts.get('Default'))).toBe(41);

    // Back: Discard drops the edits and goes back.
    await page.locator('nav a[href="#layouts"]').click();
    await page.getByRole('button', { name: 'Default', exact: true }).click();
    await editDefault(page, api, 42);
    await page.goBack();
    await expect(guard(page)).toBeVisible();
    await expect(page.getByTestId('layout-title')).toContainText('Default');
    await guard(page).getByRole('button', { name: 'Discard' }).click();
    await expect(page).toHaveURL(/#sensors$/);
    expect(titleX(api.layouts.get('Default'))).toBe(41);
    expect(await page.evaluate(() => sessionStorage.length)).toBe(0);
  });

  test('draft restored after login redirect', async ({ page }) => {
    const api = await mockApi(page, { loopback: false, networkAccess: true, authenticated: true });
    await openLayout(page, 'Default');
    await editDefault(page, api, 41);

    // The session expires: the next request (the live preview refresh) answers 401.
    api.options.authenticated = false;
    await expect(page).toHaveURL(/#login\?return=%23layouts$/);
    // Even a reload of the login page keeps the draft (only a load straight into the editor drops it).
    await page.reload();
    await page.getByLabel('Password').fill('password123');
    await page.getByRole('button', { name: 'Log in' }).click();

    await expect(page).toHaveURL(/#layouts$/);
    await expect(title(page)).toContainText('Default');
    await expect(unsaved(page)).toBeVisible();
    await expect.poll(() => titleX(lastRender(api))).toBe(41);

    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(unsaved(page)).toBeHidden();
    expect(titleX(api.layouts.get('Default'))).toBe(41);
    expect(await page.evaluate(() => sessionStorage.length)).toBe(0);
  });

  test('a restored new layout keeps its unsaved marker', async ({ page }) => {
    const api = await mockApi(page);
    await page.goto('/#layouts/new');
    await expect(title(page)).toContainText('New layout 1');
    await page.getByTestId('add-menu').getByRole('button', { name: '+ Text' }).click();
    await expect.poll(() => lastRender(api)?.items.length).toBe(1);

    await page.evaluate(() => {
      window.location.hash = '#settings'; // leaving without the in-app links (e.g. Back) skips the prompt
    });
    await page.evaluate(() => {
      window.location.hash = '#layouts';
    });
    await expect(title(page)).toContainText('New layout 1');
    await expect(unsaved(page)).toBeVisible();
    await expect.poll(() => lastRender(api)?.items[0]?.type).toBe('text');
  });
});

import type { Item, ItemType, Layout } from '../api/types';
import { LIMITS } from './schema';

export type { Item, ItemType, Layout } from '../api/types';

// All operations return new objects; inputs are never mutated.

export const ITEM_TYPES: readonly ItemType[] = [
  'text', 'sensor', 'clock', 'image', 'bar', 'segmentedBar', 'arcGauge', 'dial', 'historyGraph', 'sparkline',
];

export function newLayout(name: string, width: number, height: number): Layout {
  return { name, width, height, background: { type: 'color', color: '#101820' }, items: [] };
}

export function newId(): string {
  const alphabet = 'abcdefghijklmnopqrstuvwxyz0123456789';
  const bytes = new Uint8Array(8);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => alphabet[b % alphabet.length]).join('');
}

const font = () => ({ family: 'Inter', size: 32, weight: 400 });

/**
 * A new item with defaults matching the server model (LayoutModel.cs) where it has them. Gauge and image sizes
 * default to 0 on the server (invalid), so the editor picks usable sizes. `sensor` / `assetId` start empty and must
 * be filled in (via `extra`) before the layout validates.
 */
export function defaultItem(type: ItemType, x: number, y: number, extra: Partial<Item> = {}): Item {
  const base = { id: newId(), x, y };
  const gauge = (width: number, height: number) => ({
    sensor: '', width, height, color: '#4AAFFF', trackColor: '#33FFFFFF',
    thresholds: [], showValue: false, font: font(),
  });
  const text = { font: font(), color: '#FFFFFF', align: 'left' as const };
  let item: Item;
  switch (type) {
    case 'text': item = { ...base, type, text: 'Text', ...text }; break;
    case 'sensor': item = { ...base, type, sensor: '', showUnit: true, decimals: 0, ...text }; break;
    case 'clock': item = { ...base, type, timeZone: 'local', ...text }; break;
    case 'image': item = { ...base, type, assetId: '', width: 200, height: 200 }; break;
    case 'bar': item = { ...base, type, ...gauge(300, 24), vertical: false, rounded: true }; break;
    case 'segmentedBar': item = { ...base, type, ...gauge(300, 24), segments: 10, gap: 4, vertical: false }; break;
    case 'arcGauge': item = { ...base, type, ...gauge(200, 200), startAngle: 135, sweepAngle: 270, thickness: 16 }; break;
    case 'dial':
      item = { ...base, type, ...gauge(200, 200), startAngle: 135, sweepAngle: 270, ticks: 10, needleColor: '#FF4444' };
      break;
    case 'historyGraph': item = { ...base, type, ...gauge(400, 150), windowSeconds: 60, area: true, grid: true }; break;
    case 'sparkline': item = { ...base, type, ...gauge(160, 40), windowSeconds: 60 }; break;
  }
  return { ...item, ...extra, id: item.id, type: item.type } as Item;
}

/** A sensor readout item bound to `ref` (sensor id or "role:<role>"). */
export function sensorItem(ref: string, label: string, x: number, y: number): Item {
  return defaultItem('sensor', x, y, { sensor: ref, label });
}

const clampPos = (v: number) =>
  Math.min(LIMITS.position.max, Math.max(LIMITS.position.min, Math.round(Number.isFinite(v) ? v : 0)));

function mapItem(layout: Layout, id: string, fn: (i: Item) => Item): Layout {
  if (!layout.items.some((i) => i.id === id)) return layout;
  return { ...layout, items: layout.items.map((i) => (i.id === id ? fn(i) : i)) };
}

/** Moves an item to (x, y), rounded to whole pixels and clamped to the server position bounds. */
export function moveItem(layout: Layout, id: string, x: number, y: number): Layout {
  return mapItem(layout, id, (i) => ({ ...i, x: clampPos(x), y: clampPos(y) }));
}

export function removeItem(layout: Layout, id: string): Layout {
  return { ...layout, items: layout.items.filter((i) => i.id !== id) };
}

/** Copies an item 16 px right and down under a new id; the copy is appended (drawn on top). */
export function duplicateItem(layout: Layout, id: string): Layout {
  const src = layout.items.find((i) => i.id === id);
  if (!src) return layout;
  let nid = newId();
  while (layout.items.some((i) => i.id === nid)) nid = newId();
  const copy = structuredClone(src);
  return {
    ...layout,
    items: [...layout.items, { ...copy, id: nid, x: clampPos(src.x + 16), y: clampPos(src.y + 16) }],
  };
}

/** Appends an item (drawn on top) at a whole-pixel, in-bounds position, under a new id if its id is taken. */
export function addItem(layout: Layout, item: Item): Layout {
  let id = item.id;
  while (layout.items.some((i) => i.id === id)) id = newId();
  return { ...layout, items: [...layout.items, { ...item, id, x: clampPos(item.x), y: clampPos(item.y) }] };
}

/** Shallow-merges `patch` into the item; `id` and `type` cannot be changed. */
export function updateItem(layout: Layout, id: string, patch: Partial<Item>): Layout {
  return mapItem(layout, id, (i) => ({ ...i, ...patch, id: i.id, type: i.type }) as Item);
}

/** Converts a pointer position to layout pixels given the on-screen rect the layout canvas is drawn into. */
export function toLayoutPoint(
  clientX: number, clientY: number, rect: DOMRect, layoutWidth: number, layoutHeight: number,
): { x: number; y: number } {
  return {
    x: ((clientX - rect.left) * layoutWidth) / rect.width,
    y: ((clientY - rect.top) * layoutHeight) / rect.height,
  };
}

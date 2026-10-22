import { describe, it, expect } from 'vitest';
import {
  ITEM_TYPES, addItem, centered, defaultItem, duplicateItem, moveItem, newLayout, removeItem, sensorItem, toLayoutPoint, updateItem,
} from './model';
import type { Item, Layout } from './model';
import { LIMITS, fieldsFor } from './schema';

const COLOR = /^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$/;

function problems(item: Item): string[] {
  const out: string[] = [];
  const rec = item as unknown as Record<string, unknown>;
  for (const f of fieldsFor(item.type)) {
    const v = rec[f.key];
    if (f.kind === 'number' && typeof v === 'number') {
      if (f.min !== undefined && v < f.min) out.push(`${f.key} < min`);
      if (f.max !== undefined && v > f.max) out.push(`${f.key} > max`);
    }
    if (f.kind === 'color' && !COLOR.test(String(v))) out.push(`${f.key} not a colour`);
  }
  if ('font' in item) {
    if (item.font.size < LIMITS.fontSize.min || item.font.size > LIMITS.fontSize.max) out.push('font size');
    if (item.font.weight < LIMITS.fontWeight.min || item.font.weight > LIMITS.fontWeight.max) out.push('font weight');
    if (!item.font.family) out.push('font family');
  }
  if ('thresholds' in item && !Array.isArray(item.thresholds)) out.push('thresholds');
  if ('min' in item && item.min != null && item.max != null && item.min >= item.max) out.push('min >= max');
  return out;
}

describe('defaultItem', () => {
  it.each(ITEM_TYPES)('%s default is within the server bounds', (type) => {
    const item = defaultItem(type, 10, 20, { sensor: 'role:cpu.load', assetId: 'a1' } as Partial<Item>);
    expect(item.type).toBe(type);
    expect(item.id).toMatch(/^[a-z0-9]{8}$/);
    expect([item.x, item.y]).toEqual([10, 20]);
    expect(problems(item)).toEqual([]);
  });

  it('uses the brief defaults', () => {
    const t = defaultItem('text', 0, 0);
    expect(t).toMatchObject({ font: { family: 'Inter', size: 32, weight: 400 }, color: '#FFFFFF', align: 'left' });
    expect(defaultItem('bar', 0, 0)).toMatchObject({
      width: 300, height: 24, color: '#4AAFFF', trackColor: '#33FFFFFF', rounded: true, vertical: false,
    });
    expect(defaultItem('arcGauge', 0, 0)).toMatchObject({ width: 200, height: 200, startAngle: 135, thickness: 16 });
    expect(defaultItem('dial', 0, 0)).toMatchObject({ width: 200, height: 200, ticks: 10, needleColor: '#FF4444' });
    expect(defaultItem('historyGraph', 0, 0)).toMatchObject({ width: 400, height: 150, windowSeconds: 60 });
    expect(defaultItem('sparkline', 0, 0)).toMatchObject({ width: 160, height: 40, windowSeconds: 60 });
    expect(defaultItem('segmentedBar', 0, 0)).toMatchObject({ segments: 10, gap: 4 });
  });

  it('applies extra but never changes id or type', () => {
    const i = defaultItem('text', 0, 0, { text: 'Hi', id: 'zzz', type: 'bar' } as Partial<Item>);
    expect(i).toMatchObject({ type: 'text', text: 'Hi' });
    expect(i.id).not.toBe('zzz');
  });

  it('generates unique ids', () => {
    const ids = new Set(Array.from({ length: 500 }, () => defaultItem('text', 0, 0).id));
    expect(ids.size).toBe(500);
  });

  it('sensorItem binds a ref and label', () => {
    expect(sensorItem('role:cpu.temperature', 'CPU', 5, 6)).toMatchObject({
      type: 'sensor', sensor: 'role:cpu.temperature', label: 'CPU', x: 5, y: 6,
    });
  });
});

describe('layout operations', () => {
  const base = (): Layout => {
    const l = newLayout('L', 1280, 480);
    return { ...l, items: [defaultItem('text', 0, 0, { id: 'a' } as Partial<Item>), defaultItem('bar', 5, 5)] };
  };
  const idA = (l: Layout) => l.items[0].id;

  it('newLayout has the colour background', () => {
    expect(newLayout('N', 800, 480)).toEqual({
      name: 'N', width: 800, height: 480, background: { type: 'color', color: '#101820' }, items: [],
    });
  });

  it('moveItem clamps and rounds', () => {
    const l = base();
    const id = idA(l);
    expect(moveItem(l, id, 10.6, 20.4).items[0]).toMatchObject({ x: 11, y: 20 });
    expect(moveItem(l, id, -99999, 99999).items[0]).toMatchObject({ x: -4096, y: 8192 });
    expect(moveItem(l, id, NaN, 3).items[0]).toMatchObject({ x: 0, y: 3 });
    expect(moveItem(l, 'nope', 1, 1)).toBe(l);
  });

  it('removeItem and duplicateItem', () => {
    const l = base();
    const id = idA(l);
    expect(removeItem(l, id).items).toHaveLength(1);
    const d = duplicateItem(l, l.items[1].id);
    expect(d.items).toHaveLength(3);
    expect(d.items[2]).toMatchObject({ type: 'bar', x: 21, y: 21 });
    expect(d.items[2].id).not.toBe(l.items[1].id);
    expect(d.items[2]).not.toBe(l.items[1]);
    expect(duplicateItem(l, 'nope')).toBe(l);
  });

  it('updateItem patches but keeps id and type', () => {
    const l = base();
    const u = updateItem(l, idA(l), { text: 'New', id: 'x', type: 'bar' } as Partial<Item>);
    expect(u.items[0]).toMatchObject({ id: idA(l), type: 'text', text: 'New' });
  });

  it('never mutates its input', () => {
    const l = base();
    const snapshot = structuredClone(l);
    const id = idA(l);
    Object.freeze(l);
    l.items.forEach((i) => { Object.freeze(i); });
    moveItem(l, id, 50, 50);
    removeItem(l, id);
    duplicateItem(l, id);
    updateItem(l, id, { x: 9 });
    expect(l).toEqual(snapshot);
  });

  it('addItem appends on top, rounds and clamps the position and keeps ids unique', () => {
    const l = base();
    const added = addItem(l, { ...defaultItem('clock', 10.4, 99999), id: idA(l) });
    expect(added.items).toHaveLength(3);
    expect(added.items[2]).toMatchObject({ type: 'clock', x: 10, y: 8192 });
    expect(added.items[2].id).not.toBe(idA(l));
    const fresh = defaultItem('text', 1, 2);
    expect(addItem(l, fresh).items[2]).toEqual(fresh);
    expect(l.items).toHaveLength(2);
  });

  it('centered puts a box in the middle and a text anchor at the centre point', () => {
    const l = newLayout('L', 1280, 480);
    expect(centered(defaultItem('bar', 0, 0), l)).toMatchObject({ x: 490, y: 228 });
    expect(centered(defaultItem('clock', 0, 0), l)).toMatchObject({ x: 640, y: 240 });
    const huge = { ...defaultItem('image', 0, 0), width: 2000, height: 100 } as Item;
    expect(centered(huge, l)).toMatchObject({ x: 0, y: 190 });
  });

  it('leaves untouched items shared', () => {
    const l = base();
    expect(moveItem(l, idA(l), 1, 1).items[1]).toBe(l.items[1]);
  });
});

describe('toLayoutPoint', () => {
  const rect = { left: 100, top: 50, width: 640, height: 240 } as DOMRect;
  it('scales client pixels to layout pixels', () => {
    expect(toLayoutPoint(100, 50, rect, 1280, 480)).toEqual({ x: 0, y: 0 });
    expect(toLayoutPoint(420, 170, rect, 1280, 480)).toEqual({ x: 640, y: 240 });
    expect(toLayoutPoint(740, 290, rect, 1280, 480)).toEqual({ x: 1280, y: 480 });
  });
});

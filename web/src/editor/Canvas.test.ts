import { describe, expect, it } from 'vitest';
import type { Layout } from '../api/types';
import { hitBox } from './Canvas';
import { defaultItem, newLayout } from './model';

const text = { ...defaultItem('text', 100, 50), id: 't', text: '' };
const bar = { ...defaultItem('bar', 10, 20, { sensor: 'cpu/t' }), id: 'b' };
const measuredLayout: Layout = { ...newLayout('A', 1280, 480), items: [text, bar] };

describe('hitBox', () => {
  it('gives an empty text (measured 0×0) a 24×24 box centred on its point', () => {
    const r = hitBox(text, { layout: measuredLayout, bounds: { t: { x: 100, y: 50, width: 0, height: 0 } } });
    expect(r).toEqual({ x: 88, y: 38, width: 24, height: 24 });
  });

  it('pads measured bounds by 8 px on every side', () => {
    const r = hitBox(bar, { layout: measuredLayout, bounds: { b: { x: 10, y: 20, width: 300, height: 24 } } });
    expect(r).toEqual({ x: 2, y: 12, width: 316, height: 40 });
  });

  it('follows an item moved since it was measured', () => {
    const moved = { ...bar, x: 60, y: 25 };
    const r = hitBox(moved, { layout: measuredLayout, bounds: { b: { x: 10, y: 20, width: 300, height: 24 } } });
    expect(r).toEqual({ x: 52, y: 17, width: 316, height: 40 });
  });

  it('uses the declared box of an item that has not been measured', () => {
    expect(hitBox(bar, null)).toEqual({ x: 2, y: 12, width: 316, height: 40 });
    expect(hitBox(text, null)).toEqual({ x: 88, y: 38, width: 24, height: 24 });
  });
});

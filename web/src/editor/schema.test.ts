import { describe, it, expect } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';
import { ITEM_TYPES, defaultItem } from './model';
import { LIMITS, fieldsFor } from './schema';

const layoutsDir = path.resolve(process.cwd(), '..', 'src', 'Glance.Core', 'Layouts');
const limitsCs = fs.readFileSync(path.join(layoutsDir, 'LayoutLimits.cs'), 'utf8');
const validatorCs = fs.readFileSync(path.join(layoutsDir, 'LayoutValidator.cs'), 'utf8');

/** Reads `const <T> MinX = a, MaxX = b;` pairs from LayoutLimits.cs. */
function csBounds(name: string): { min: number; max: number } {
  const m = new RegExp(`Min${name}\\s*=\\s*(-?\\d+)\\s*,\\s*Max${name}\\s*=\\s*(-?\\d+)`).exec(limitsCs);
  if (!m) throw new Error(`Min${name}/Max${name} not found in LayoutLimits.cs`);
  return { min: Number(m[1]), max: Number(m[2]) };
}

describe('bounds match LayoutLimits.cs', () => {
  it.each([
    ['position', 'Position'],
    ['itemSize', 'ItemSize'],
    ['fontSize', 'FontSize'],
    ['fontWeight', 'FontWeight'],
    ['ticks', 'Ticks'],
    ['segments', 'Segments'],
    ['gap', 'Gap'],
    ['thickness', 'Thickness'],
    ['startAngle', 'StartAngle'],
    ['sweepAngle', 'SweepAngle'],
  ] as const)('%s', (key, csName) => {
    expect(LIMITS[key]).toEqual(csBounds(csName));
  });

  it('literal ranges in LayoutValidator.cs', () => {
    expect(validatorCs).toContain('l.Width is < 1 or > 4096');
    expect(validatorCs).toContain('l.Height is < 1 or > 4096');
    expect(LIMITS.layoutSize).toEqual({ min: 1, max: 4096 });
    expect(validatorCs).toContain('s.Decimals is < 0 or > 3');
    expect(LIMITS.decimals).toEqual({ min: 0, max: 3 });
    expect(validatorCs.match(/WindowSeconds is < 1 or > 1800/g)).toHaveLength(2);
    expect(LIMITS.windowSeconds).toEqual({ min: 1, max: 1800 });
  });
});

describe('fieldsFor', () => {
  it.each(ITEM_TYPES)('covers %s and every editable property exists on the default item', (type) => {
    const fields = fieldsFor(type);
    expect(fields.length).toBeGreaterThan(0);
    const keys = fields.map((f) => f.key);
    expect(new Set(keys).size).toBe(keys.length);
    const item = defaultItem(type, 0, 0) as unknown as Record<string, unknown>;
    for (const f of fields) {
      // optional properties (label, format, min, max) are absent from defaults
      if (!['label', 'format', 'min', 'max'].includes(f.key)) expect(item, f.key).toHaveProperty(f.key);
    }
    expect(keys).toContain('x');
  });

  it('numeric fields carry bounds, selects carry options', () => {
    for (const type of ITEM_TYPES)
      for (const f of fieldsFor(type)) {
        if (f.kind === 'select') expect(f.options?.length).toBeGreaterThan(0);
        if (f.kind === 'number' && !['min', 'max'].includes(f.key)) {
          expect(f.min, `${type}.${f.key}`).toBeDefined();
          expect(f.max, `${type}.${f.key}`).toBeDefined();
        }
      }
  });

  it('exposes the type-specific properties', () => {
    const keys = (t: Parameters<typeof fieldsFor>[0]) => fieldsFor(t).map((f) => f.key);
    expect(keys('segmentedBar')).toEqual(expect.arrayContaining(['segments', 'gap', 'vertical', 'thresholds']));
    expect(keys('arcGauge')).toEqual(expect.arrayContaining(['startAngle', 'sweepAngle', 'thickness']));
    expect(keys('dial')).toEqual(expect.arrayContaining(['ticks', 'needleColor']));
    expect(keys('historyGraph')).toEqual(expect.arrayContaining(['windowSeconds', 'area', 'grid']));
    expect(keys('clock')).toEqual(expect.arrayContaining(['format', 'timeZone']));
    expect(keys('image')).toEqual(expect.arrayContaining(['assetId', 'width', 'height']));
  });
});

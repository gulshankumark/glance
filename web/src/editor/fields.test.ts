import { describe, expect, it } from 'vitest';
import type { SensorsResponse } from '../api/types';
import { describeItem } from './catalog';
import { addThreshold, isColor, parseNumber, withRgb } from './fields';
import { defaultItem } from './model';
import { fieldsFor, LIMITS } from './schema';

const field = (type: Parameters<typeof fieldsFor>[0], key: string) => fieldsFor(type).find((f) => f.key === key)!;

describe('parseNumber', () => {
  it('clamps to the schema bounds and rounds whole-number fields', () => {
    const width = field('bar', 'width');
    expect(parseNumber(width, '250')).toBe(250);
    expect(parseNumber(width, '99999')).toBe(LIMITS.itemSize.max);
    expect(parseNumber(width, '0')).toBe(LIMITS.itemSize.min);
    expect(parseNumber(width, '12.6')).toBe(13);
    expect(parseNumber(field('sensor', 'decimals'), '7')).toBe(3);
  });

  it('rejects text that is not a number (undefined keeps the old value)', () => {
    expect(parseNumber(field('bar', 'width'), 'abc')).toBeUndefined();
    expect(parseNumber(field('bar', 'width'), '')).toBeUndefined();
  });

  it('lets an unbounded gauge minimum/maximum be cleared (null = automatic) and keep fractions', () => {
    expect(parseNumber(field('bar', 'min'), '')).toBeNull();
    expect(parseNumber(field('bar', 'max'), '  ')).toBeNull();
    expect(parseNumber(field('bar', 'max'), '99.5')).toBe(99.5);
  });
});

describe('colours', () => {
  it('accepts #RRGGBB and #AARRGGBB only', () => {
    expect(isColor('#A0b0C0')).toBe(true);
    expect(isColor('#33FFFFFF')).toBe(true);
    expect(isColor('#FFF')).toBe(false);
    expect(isColor('red')).toBe(false);
  });

  it('withRgb replaces the colour but keeps the alpha', () => {
    expect(withRgb('#33FFFFFF', '#102030')).toBe('#33102030');
    expect(withRgb('#FFFFFF', '#102030')).toBe('#102030');
  });
});

describe('thresholds', () => {
  it('adds a row above the last one', () => {
    expect(addThreshold([])).toEqual([{ from: 0, color: '#FF4444' }]);
    expect(addThreshold([{ from: 70, color: '#FFAA00' }])).toEqual([
      { from: 70, color: '#FFAA00' },
      { from: 80, color: '#FF4444' },
    ]);
  });
});

describe('describeItem', () => {
  const catalog: SensorsResponse = {
    sensors: [{ id: 'gpu/l', hardware: 'Arc', hardwareKind: 'gpu', name: 'GPU Core', kind: 'load', unit: '%', value: 1, alias: null }],
    roles: {},
    pins: {},
    diagnostics: [],
  };

  it('names the type and what the item shows, never its id', () => {
    expect(describeItem(defaultItem('sensor', 0, 0, { sensor: 'role:cpu.temperature', label: 'CPU' }), catalog))
      .toBe('Sensor — CPU temperature');
    expect(describeItem(defaultItem('arcGauge', 0, 0, { sensor: 'role:gpu.load' }), catalog)).toBe('Arc gauge — GPU load');
    expect(describeItem(defaultItem('bar', 0, 0, { sensor: 'gpu/l' }), catalog)).toBe('Bar — GPU Core');
    expect(describeItem(defaultItem('text', 0, 0, { text: 'Hello' }), catalog)).toBe('Text — Hello');
    expect(describeItem(defaultItem('text', 0, 0, { text: '' }), catalog)).toBe('Text — (empty)');
    expect(describeItem(defaultItem('clock', 0, 0), catalog)).toBe('Clock — Local time');
    expect(describeItem(defaultItem('image', 0, 0, { assetId: 'x', width: 30, height: 20 }), catalog)).toBe('Image — 30×20');
  });
});

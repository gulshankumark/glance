import { describe, expect, it } from 'vitest';
import type { SensorsResponse } from '../api/types';
import { describeItem } from './catalog';
import { addThreshold, fromShownTemperature, isColor, isTemperatureRef, parseNumber, toShownTemperature, withRgb } from './fields';
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

describe('temperature fields', () => {
  it('shows °C values as they are and °F values rounded to 0.1', () => {
    expect(toShownTemperature(37.78, 'celsius')).toBe(37.78);
    expect(toShownTemperature(50, 'fahrenheit')).toBe(122);
    expect(toShownTemperature(37.78, 'fahrenheit')).toBe(100);
    expect(toShownTemperature(-40, 'fahrenheit')).toBe(-40);
  });

  it('stores what was typed in °C, rounded to 0.01', () => {
    expect(fromShownTemperature(80, 'celsius')).toBe(80);
    expect(fromShownTemperature(122, 'fahrenheit')).toBe(50);
    expect(fromShownTemperature(100, 'fahrenheit')).toBe(37.78);
  });

  it('a typed °F value shows again as typed', () => {
    for (const f of [100, 150.5, 0, -3.2, 212, 99.9]) {
      expect(toShownTemperature(fromShownTemperature(f, 'fahrenheit'), 'fahrenheit')).toBe(f);
    }
  });

  it('knows which bindings are temperatures, roles included', () => {
    const catalog: SensorsResponse = {
      sensors: [
        { id: 'cpu/t', hardware: 'h', hardwareKind: 'cpu', name: 'n', kind: 'temperature', unit: '°C', value: 1, alias: null },
        { id: 'cpu/l', hardware: 'h', hardwareKind: 'cpu', name: 'n', kind: 'load', unit: '%', value: 1, alias: null },
      ],
      roles: { 'cpu.temperature': 'cpu/t', 'cpu.load': 'cpu/l', 'gpu.temperature': null },
      pins: {},
      diagnostics: [],
    };
    expect(isTemperatureRef('cpu/t', catalog)).toBe(true);
    expect(isTemperatureRef('role:cpu.temperature', catalog)).toBe(true);
    expect(isTemperatureRef('cpu/l', catalog)).toBe(false);
    expect(isTemperatureRef('role:cpu.load', catalog)).toBe(false);
    expect(isTemperatureRef('role:gpu.temperature', catalog)).toBe(true);
    expect(isTemperatureRef('unknown', catalog)).toBe(false);
    expect(isTemperatureRef('cpu/t', null)).toBe(false);
  });
});

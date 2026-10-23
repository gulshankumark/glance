import { describe, expect, it } from 'vitest';
import type { SensorsResponse } from '../api/types';
import { matches, roleLabel, sensorGroups, sensorName, sensorOptions } from './catalog';

const catalog: SensorsResponse = {
  sensors: [
    { id: 'cpu/t', hardware: 'Ryzen', hardwareKind: 'cpu', name: 'CPU Package', kind: 'temperature', unit: '°C', value: 55, alias: 'CPU' },
    { id: 'gpu/l', hardware: 'Arc', hardwareKind: 'gpu', name: 'GPU Core', kind: 'load', unit: '%', value: 12, alias: null },
    { id: 'mb/f', hardware: 'Board', hardwareKind: 'motherboard', name: 'Fan #1', kind: 'fan', unit: 'RPM', value: null, alias: null },
  ],
  roles: { 'cpu.temperature': 'cpu/t', 'gpu.load': null },
  pins: {},
  diagnostics: [],
};

describe('catalog', () => {
  it('names roles in plain words', () => {
    expect(roleLabel('cpu.temperature')).toBe('CPU temperature');
    expect(roleLabel('gpu.memory.clock')).toBe('GPU memory clock');
    expect(roleLabel('cpu.power.package')).toBe('CPU package power');
    expect(roleLabel('made.up')).toBe('made.up');
  });

  it('names a sensor ref by alias, then name, then the ref itself', () => {
    expect(sensorName('cpu/t', catalog)).toBe('CPU');
    expect(sensorName('gpu/l', catalog)).toBe('GPU Core');
    expect(sensorName('role:gpu.load', catalog)).toBe('GPU load');
    expect(sensorName('gone/x', catalog)).toBe('gone/x');
    expect(sensorName('cpu/t', null)).toBe('cpu/t');
  });

  it('groups sensors by hardware kind in a fixed order, leaving out empty groups', () => {
    const groups = sensorGroups(catalog);
    expect(groups.map((g) => g.label)).toEqual(['CPU', 'GPU', 'Motherboard']);
    expect(groups[0].sensors.map((s) => s.id)).toEqual(['cpu/t']);
  });

  it('lists roles first, then sensors, for pickers', () => {
    const options = sensorOptions(catalog);
    expect(options.map((o) => o.value)).toEqual(['role:cpu.temperature', 'role:gpu.load', 'cpu/t', 'gpu/l', 'mb/f']);
    expect(options[0].label).toBe('Role: CPU temperature');
    expect(options[2].label).toBe('CPU — Ryzen');
  });

  it('matches search text against every word, ignoring case', () => {
    expect(matches('GPU Core Arc', 'core')).toBe(true);
    expect(matches('GPU Core Arc', 'arc gpu')).toBe(true);
    expect(matches('GPU Core Arc', 'cpu')).toBe(false);
    expect(matches('anything', '  ')).toBe(true);
  });
});

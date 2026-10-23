import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import type { SensorsResponse } from '../api/types';
import { newLayout } from './model';
import { useTreeGroups } from './SensorTree';
import { useDirty } from './useDirty';

const catalog: SensorsResponse = {
  sensors: [{ id: 'a', hardware: 'H', hardwareKind: 'cpu', name: 'A', kind: 'load', unit: '%', value: 1, alias: null }],
  roles: { 'cpu.load': 'a' },
  pins: {},
  diagnostics: [],
};

afterEach(() => vi.restoreAllMocks());

describe('editor memoization', () => {
  it('the sensor tree groups are rebuilt only when the catalog or the query change', () => {
    const { result, rerender } = renderHook(({ c, q }) => useTreeGroups(c, q), { initialProps: { c: catalog, q: '' } });
    const first = result.current;
    expect(first.map((g) => g.label)).toEqual(['Roles', 'CPU']);
    rerender({ c: catalog, q: '' });
    expect(result.current).toBe(first);
    rerender({ c: catalog, q: 'zzz' });
    expect(result.current).toEqual([]);
  });

  it('the dirty check serializes the draft only when the draft changes', () => {
    const draft = newLayout('L', 10, 10);
    const saved = JSON.stringify(draft);
    const { result, rerender } = renderHook(({ d }) => useDirty(d, saved, false), { initialProps: { d: draft } });
    expect(result.current).toBe(false);

    const stringify = vi.spyOn(JSON, 'stringify');
    rerender({ d: draft });
    rerender({ d: draft });
    expect(stringify).not.toHaveBeenCalled();

    rerender({ d: { ...draft, width: 11 } });
    expect(result.current).toBe(true);
    expect(stringify).toHaveBeenCalledTimes(1);
  });

  it('a never-saved layout is always dirty', () => {
    const { result } = renderHook(() => useDirty(newLayout('L', 1, 1), '', true));
    expect(result.current).toBe(true);
  });
});

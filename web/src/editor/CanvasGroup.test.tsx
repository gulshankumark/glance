import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import type { Layout } from '../api/types';
import { Canvas } from './Canvas';
import { defaultItem, newLayout } from './model';

// The canvas renders the draft through the API; these tests only look at draft changes, so requests never answer.
vi.mock('../api/client', async (importActual) => ({
  ...(await importActual<typeof import('../api/client')>()),
  api: () => new Promise(() => undefined),
  postBlob: () => new Promise(() => undefined),
}));

afterEach(cleanup);

const layout: Layout = {
  ...newLayout('L', 1280, 480),
  items: [
    { ...defaultItem('text', 10, 10), id: 'a' },
    { ...defaultItem('bar', 20, 20), id: 'b' },
    { ...defaultItem('clock', 30, 30), id: 'c' },
  ],
};

function setup(selectedIds: string[]) {
  const onChange = vi.fn<(l: Layout) => void>();
  const onSelect = vi.fn<(ids: string[]) => void>();
  render(<Canvas layout={layout} selectedIds={selectedIds} onSelect={onSelect} onChange={onChange} />);
  return { onChange, onSelect, stage: screen.getByTestId('layout-canvas') };
}

const ids = (l: Layout) => l.items.map((i) => i.id);

describe('Canvas group actions are one draft change each', () => {
  it('Delete removes the whole selection in one change', () => {
    const { onChange, onSelect, stage } = setup(['a', 'c']);
    fireEvent.keyDown(stage, { key: 'Delete' });
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(ids(onChange.mock.calls[0][0])).toEqual(['b']);
    expect(onSelect).toHaveBeenCalledWith([]);
  });

  it('Ctrl+D duplicates the whole selection in one change and selects the copies', () => {
    const { onChange, onSelect, stage } = setup(['a', 'c']);
    fireEvent.keyDown(stage, { key: 'd', ctrlKey: true });
    expect(onChange).toHaveBeenCalledTimes(1);
    const next = onChange.mock.calls[0][0];
    expect(next.items).toHaveLength(5);
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0]).toEqual(ids(next).slice(3));
  });

  it('an arrow moves the whole selection in one change', () => {
    const { onChange, stage } = setup(['a', 'b']);
    fireEvent.keyDown(stage, { key: 'ArrowDown', shiftKey: true });
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0].items.map((i) => [i.id, i.x, i.y])).toEqual([
      ['a', 10, 20], ['b', 20, 30], ['c', 30, 30],
    ]);
  });
});

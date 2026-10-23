import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import type { Layout } from '../api/types';
import { Canvas } from './Canvas';
import { defaultItem, newLayout } from './model';

// The canvas renders the draft through the API; these tests only look at the drag, so requests never answer.
vi.mock('../api/client', async (importActual) => ({
  ...(await importActual<typeof import('../api/client')>()),
  api: () => new Promise(() => undefined),
  postBlob: () => new Promise(() => undefined),
}));

// jsdom has neither PointerEvent nor pointer capture.
class TestPointerEvent extends MouseEvent {
  pointerId: number;
  constructor(type: string, init: PointerEventInit = {}) {
    super(type, init);
    this.pointerId = init.pointerId ?? 0;
  }
}
const saved = { PointerEvent: globalThis.PointerEvent, capture: Element.prototype.setPointerCapture };
beforeAll(() => {
  globalThis.PointerEvent = TestPointerEvent as unknown as typeof PointerEvent;
  Element.prototype.setPointerCapture = () => undefined;
});
afterAll(() => {
  globalThis.PointerEvent = saved.PointerEvent;
  Element.prototype.setPointerCapture = saved.capture;
});
afterEach(cleanup);

// A 300×24 bar at (20, 20): its centre is at x 170.
const layout: Layout = {
  ...newLayout('L', 1280, 480),
  items: [{ ...defaultItem('bar', 20, 20), id: 'b' }, { ...defaultItem('text', 900, 400), id: 't' }],
};

function setup(selectedIds: string[]) {
  const onChange = vi.fn<(l: Layout) => void>();
  const onSelect = vi.fn<(ids: string[]) => void>();
  render(<Canvas layout={layout} selectedIds={selectedIds} onSelect={onSelect} onChange={onChange} />);
  const stage = screen.getByTestId('layout-canvas');
  // Shown at layout scale: 1 screen px = 1 layout px.
  stage.getBoundingClientRect = () => ({ left: 0, top: 0, width: 1280, height: 480 }) as DOMRect;
  return { onChange, onSelect, box: screen.getByTestId('item-box-b') };
}

const at = (clientX: number) => ({ pointerId: 1, button: 0, clientX, clientY: 100 });

describe('Canvas drag ends when pointer capture is lost', () => {
  it('clears the guides and the drag without a pointerup', () => {
    const { onChange, box } = setup(['b']);
    fireEvent.pointerDown(box, at(100));
    fireEvent.pointerMove(box, at(570)); // centre 640: on the canvas centre
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(screen.getAllByTestId('snap-guide')).toHaveLength(1);

    fireEvent.lostPointerCapture(box, at(570));
    expect(screen.queryAllByTestId('snap-guide')).toHaveLength(0);
    fireEvent.pointerMove(box, at(700));
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('does not narrow a multi-selection (only a pointerup does)', () => {
    const { onSelect, box } = setup(['b', 't']);
    fireEvent.pointerDown(box, at(100));
    fireEvent.lostPointerCapture(box, at(100));
    fireEvent.pointerUp(box, at(100));
    expect(onSelect).not.toHaveBeenCalled();
  });
});

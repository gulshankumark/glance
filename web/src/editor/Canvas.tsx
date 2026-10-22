import React, { useEffect, useRef, useState } from 'react';
import { api, ApiError, postBlob } from '../api/client';
import type { Item, Layout } from '../api/types';
import { duplicateItem, moveItem, removeItem, toLayoutPoint } from './model';
import { createRenderScheduler, type RenderScheduler } from './scheduler';

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}
/** POST /api/render/bounds answer: layout-space bounds keyed by item id. */
export type Bounds = Record<string, Rect>;

const LIVE_REFRESH_MS = 1000;
/** Hit boxes are padded and never smaller than this (layout pixels): empty text measures 0×0, captions lie outside. */
const HIT_PAD = 8;
const HIT_MIN = 24;

interface Rendered {
  url: string;
  bounds: Bounds;
  /** The layout the image and bounds were made from. */
  layout: Layout;
}

/** Renders the draft (image + bounds in one cycle) on every change and every second while the page is visible. */
function useRenderedDraft(layout: Layout): { rendered: Rendered | null; error: string | null } {
  const [rendered, setRendered] = useState<Rendered | null>(null);
  const [error, setError] = useState<string | null>(null);
  const layoutRef = useRef(layout);
  layoutRef.current = layout;
  const schedulerRef = useRef<RenderScheduler<Layout> | null>(null);

  useEffect(() => {
    let url: string | null = null;
    const scheduler = createRenderScheduler(
      async (l: Layout, signal: AbortSignal) => {
        // One request cycle: when either call fails, the other is cancelled so nothing stays in flight.
        const inner = new AbortController();
        const onAbort = () => inner.abort();
        signal.addEventListener('abort', onAbort);
        try {
          const [blob, bounds] = await Promise.all([
            postBlob('/api/render', l, { signal: inner.signal }),
            api<Bounds>('POST', '/api/render/bounds', l, { signal: inner.signal }),
          ]);
          return { blob, bounds, layout: l };
        } catch (e) {
          inner.abort();
          throw e;
        } finally {
          signal.removeEventListener('abort', onAbort);
        }
      },
      (r) => {
        const old = url;
        url = URL.createObjectURL(r.blob);
        setRendered({ url, bounds: r.bounds, layout: r.layout });
        setError(null);
        if (old) URL.revokeObjectURL(old);
      },
      {
        onError: (e) =>
          setError(e instanceof ApiError ? e.errors.join('; ') : 'The preview could not be updated.'),
      },
    );
    schedulerRef.current = scheduler;

    const refresh = () => {
      if (document.visibilityState === 'visible') scheduler.request(layoutRef.current);
    };
    const timer = setInterval(refresh, LIVE_REFRESH_MS);
    document.addEventListener('visibilitychange', refresh);
    return () => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', refresh);
      scheduler.dispose();
      schedulerRef.current = null;
      if (url) URL.revokeObjectURL(url);
    };
  }, []);

  useEffect(() => {
    schedulerRef.current?.request(layout);
  }, [layout]);

  return { rendered, error };
}

/** Declared box of an item, for items the server has not measured (yet). */
function declaredBox(item: Item): Rect {
  const sized = 'width' in item ? { width: item.width, height: item.height } : { width: 0, height: 0 };
  return { x: item.x, y: item.y, ...sized };
}

/** The selection hit box of an item in layout pixels: measured bounds shifted by any move since, padded. */
export function hitBox(item: Item, rendered: Pick<Rendered, 'bounds' | 'layout'> | null): Rect {
  const measured = rendered?.bounds[item.id];
  const then = rendered?.layout.items.find((i) => i.id === item.id);
  const r = measured && then
    ? { ...measured, x: measured.x + item.x - then.x, y: measured.y + item.y - then.y }
    : declaredBox(item);
  const grow = (pos: number, len: number) => {
    let p = pos - HIT_PAD;
    let l = len + 2 * HIT_PAD;
    if (l < HIT_MIN) {
      p -= (HIT_MIN - l) / 2;
      l = HIT_MIN;
    }
    return [p, l] as const;
  };
  const [x, width] = grow(r.x, r.width);
  const [y, height] = grow(r.y, r.height);
  return { x, y, width, height };
}

export interface CanvasProps {
  layout: Layout;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  onChange: (layout: Layout) => void;
}

interface Drag {
  id: string;
  pointerId: number;
  start: { x: number; y: number };
  origin: { x: number; y: number };
}

/**
 * The layout as the server draws it, scaled to the available width, with a selection box over every item:
 * click selects, drag moves, arrows nudge (Shift: 10 px), Delete removes, Ctrl/Cmd+D duplicates.
 */
export const Canvas: React.FC<CanvasProps> = ({ layout, selectedId, onSelect, onChange }) => {
  const { rendered, error } = useRenderedDraft(layout);
  const stageRef = useRef<HTMLDivElement>(null);
  const layoutRef = useRef(layout);
  layoutRef.current = layout;
  const dragRef = useRef<Drag | null>(null);

  const pointAt = (e: React.PointerEvent) => {
    const l = layoutRef.current;
    return toLayoutPoint(e.clientX, e.clientY, stageRef.current!.getBoundingClientRect(), l.width, l.height);
  };

  const onBoxPointerDown = (e: React.PointerEvent<HTMLDivElement>, item: Item) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    stageRef.current?.focus({ preventScroll: true });
    onSelect(item.id);
    e.currentTarget.setPointerCapture(e.pointerId);
    dragRef.current = { id: item.id, pointerId: e.pointerId, start: pointAt(e), origin: { x: item.x, y: item.y } };
  };

  const onBoxPointerMove = (e: React.PointerEvent) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    const p = pointAt(e);
    const x = Math.round(drag.origin.x + p.x - drag.start.x);
    const y = Math.round(drag.origin.y + p.y - drag.start.y);
    const current = layoutRef.current.items.find((i) => i.id === drag.id);
    if (!current || (current.x === x && current.y === y)) return;
    onChange(moveItem(layoutRef.current, drag.id, x, y));
  };

  const endDrag = (e: React.PointerEvent) => {
    if (dragRef.current?.pointerId === e.pointerId) dragRef.current = null;
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    const item = layout.items.find((i) => i.id === selectedId);
    if (!item) return;
    const step = e.shiftKey ? 10 : 1;
    const nudge: Record<string, [number, number]> = {
      ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step],
    };
    if (nudge[e.key]) {
      e.preventDefault();
      const [dx, dy] = nudge[e.key];
      onChange(moveItem(layout, item.id, item.x + dx, item.y + dy));
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      e.preventDefault();
      onSelect(null);
      onChange(removeItem(layout, item.id));
    } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') {
      e.preventDefault();
      const next = duplicateItem(layout, item.id);
      onChange(next);
      onSelect(next.items[next.items.length - 1].id);
    }
  };

  const pct = (v: number, of: number) => `${(v / of) * 100}%`;

  return (
    <div>
      <div
        ref={stageRef}
        data-testid="layout-canvas"
        tabIndex={0}
        aria-label={`Canvas of ${layout.name}`}
        onKeyDown={onKeyDown}
        onPointerDown={(e) => {
          if (e.button === 0) onSelect(null);
        }}
        style={{
          position: 'relative',
          width: '100%',
          maxWidth: `calc(70vh * ${layout.width / layout.height})`,
          aspectRatio: `${layout.width} / ${layout.height}`,
          margin: '0 auto',
          background: '#000',
          border: '1px solid var(--surface-border)',
          borderRadius: 4,
          overflow: 'hidden',
          touchAction: 'none',
          userSelect: 'none',
          outlineOffset: 2,
        }}
      >
        {rendered && (
          <img
            data-testid="layout-preview"
            src={rendered.url}
            alt={`Preview of ${layout.name}`}
            draggable={false}
            style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', pointerEvents: 'none' }}
          />
        )}
        {layout.items.map((item) => {
          const b = hitBox(item, rendered);
          const selected = item.id === selectedId;
          return (
            <div
              key={item.id}
              data-testid={`item-box-${item.id}`}
              role="button"
              tabIndex={-1}
              aria-label={`${item.type} ${item.id}`}
              aria-pressed={selected}
              onPointerDown={(e) => onBoxPointerDown(e, item)}
              onPointerMove={onBoxPointerMove}
              onPointerUp={endDrag}
              onPointerCancel={endDrag}
              style={{
                position: 'absolute',
                left: pct(b.x, layout.width),
                top: pct(b.y, layout.height),
                width: pct(b.width, layout.width),
                height: pct(b.height, layout.height),
                cursor: 'move',
                border: selected ? '2px solid var(--accent)' : '1px dashed rgba(255, 255, 255, 0.3)',
                borderRadius: 2,
                zIndex: selected ? 1 : undefined,
              }}
            />
          );
        })}
      </div>
      {error && (
        <p role="status" style={{ marginTop: 'var(--space-2)', fontSize: '0.85rem', color: 'var(--warning)' }}>
          Preview not updated: {error}
        </p>
      )}
    </div>
  );
};

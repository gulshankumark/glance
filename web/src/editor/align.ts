/** A rectangle in layout pixels. */
export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** A snap guide in layout pixels; `axis: 'x'` is a vertical line at x = at, `axis: 'y'` a horizontal one at y = at. */
export type Guide = { axis: 'x' | 'y'; at: number };

/** The smallest rectangle containing every one of `rects` (at least one). */
export function unionRect(rects: readonly Rect[]): Rect {
  const x = Math.min(...rects.map((r) => r.x));
  const y = Math.min(...rects.map((r) => r.y));
  const right = Math.max(...rects.map((r) => r.x + r.width));
  const bottom = Math.max(...rects.map((r) => r.y + r.height));
  return { x, y, width: right - x, height: bottom - y };
}

const lines = (start: number, length: number) => [start, start + length / 2, start + length];
const EPSILON = 1e-6;

/** Snaps one axis: the nearest (moving edge, target line) pair within the threshold shifts `delta` by its distance. */
function snapAxis(start: number, length: number, delta: number, targets: number[], threshold: number) {
  let best: number | null = null;
  for (const edge of lines(start + delta, length)) {
    for (const t of targets) {
      const d = t - edge;
      if (Math.abs(d) <= threshold && (best === null || Math.abs(d) < Math.abs(best))) best = d;
    }
  }
  const snapped = delta + (best ?? 0);
  const edges = lines(start + snapped, length);
  const hit = best === null ? [] : targets.filter((t) => edges.some((e) => Math.abs(e - t) < EPSILON));
  return { delta: snapped, hit: [...new Set(hit)] };
}

/**
 * Moves `moving` by (dx, dy), then snaps each axis independently: its start, centre and end against the start, centre
 * and end of every target and of the canvas. The nearest candidate within `threshold` wins; the guides are every
 * target or canvas line the snapped rectangle touches.
 */
export function snapMove(
  moving: Rect,
  targets: readonly Rect[],
  canvas: { width: number; height: number },
  dx: number,
  dy: number,
  threshold: number,
): { dx: number; dy: number; guides: Guide[] } {
  const xs = [...lines(0, canvas.width), ...targets.flatMap((t) => lines(t.x, t.width))];
  const ys = [...lines(0, canvas.height), ...targets.flatMap((t) => lines(t.y, t.height))];
  const x = snapAxis(moving.x, moving.width, dx, xs, threshold);
  const y = snapAxis(moving.y, moving.height, dy, ys, threshold);
  return {
    dx: x.delta,
    dy: y.delta,
    guides: [...x.hit.map((at) => ({ axis: 'x' as const, at })), ...y.hit.map((at) => ({ axis: 'y' as const, at }))],
  };
}

export type AlignMode = 'left' | 'centre' | 'right' | 'top' | 'middle' | 'bottom';

type Moves = Record<string, { dx: number; dy: number }>;

/** Moves that line every rectangle up on one edge or centre of their union rectangle. */
export function alignMoves(rects: Record<string, Rect>, mode: AlignMode): Moves {
  const all = Object.values(rects);
  if (all.length === 0) return {};
  const u = unionRect(all);
  const moves: Moves = {};
  for (const [id, r] of Object.entries(rects)) {
    switch (mode) {
      case 'left': moves[id] = { dx: u.x - r.x, dy: 0 }; break;
      case 'centre': moves[id] = { dx: u.x + u.width / 2 - (r.x + r.width / 2), dy: 0 }; break;
      case 'right': moves[id] = { dx: u.x + u.width - (r.x + r.width), dy: 0 }; break;
      case 'top': moves[id] = { dx: 0, dy: u.y - r.y }; break;
      case 'middle': moves[id] = { dx: 0, dy: u.y + u.height / 2 - (r.y + r.height / 2) }; break;
      case 'bottom': moves[id] = { dx: 0, dy: u.y + u.height - (r.y + r.height) }; break;
    }
  }
  return moves;
}

/**
 * Moves that space the rectangles evenly along one axis, ordered by their start: the first and the last stay put and
 * every gap between neighbours becomes (last end − first start − sum of sizes) / (n − 1), negative when they overlap.
 * Fewer than three rectangles do not move.
 */
export function distributeMoves(rects: Record<string, Rect>, axis: 'x' | 'y'): Moves {
  const start = (r: Rect) => (axis === 'x' ? r.x : r.y);
  const size = (r: Rect) => (axis === 'x' ? r.width : r.height);
  const entries = Object.entries(rects).sort(([, a], [, b]) => start(a) - start(b));
  const moves: Moves = Object.fromEntries(entries.map(([id]) => [id, { dx: 0, dy: 0 }]));
  if (entries.length < 3) return moves;
  const first = entries[0][1];
  const last = entries[entries.length - 1][1];
  const total = entries.reduce((s, [, r]) => s + size(r), 0);
  const gap = (start(last) + size(last) - start(first) - total) / (entries.length - 1);
  let at = start(first);
  for (const [id, r] of entries.slice(0, -1)) {
    const d = at - start(r);
    moves[id] = axis === 'x' ? { dx: d, dy: 0 } : { dx: 0, dy: d };
    at += size(r) + gap;
  }
  return moves;
}

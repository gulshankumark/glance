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

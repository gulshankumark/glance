import { describe, it, expect } from 'vitest';
import { alignMoves, distributeMoves, snapMove, unionRect, type Rect } from './align';

const CANVAS = { width: 1280, height: 480 };
const MOVING = { x: 10, y: 10, width: 100, height: 20 };
const TARGET = { x: 200, y: 300, width: 50, height: 50 };

describe('unionRect', () => {
  it('spans every rectangle', () => {
    expect(unionRect([{ x: 10, y: 20, width: 5, height: 5 }, { x: 0, y: 30, width: 50, height: 10 }]))
      .toEqual({ x: 0, y: 20, width: 50, height: 20 });
  });

  it('of one rectangle is that rectangle', () => {
    expect(unionRect([MOVING])).toEqual(MOVING);
  });
});

describe('snapMove', () => {
  it('snaps the left edge to a target left edge within the threshold', () => {
    const r = snapMove(MOVING, [TARGET], CANVAS, 187, 0, 6);
    expect(r.dx).toBe(190);
    expect(r.dy).toBe(0);
    expect(r.guides).toContainEqual({ axis: 'x', at: 200 });
    expect(r.guides.every((g) => g.axis === 'x')).toBe(true);
  });

  it('snaps the centre to the canvas centre', () => {
    // Centre lands at 10 + 50 + 576 = 636, 4 px from 640.
    const r = snapMove(MOVING, [TARGET], CANVAS, 576, 0, 6);
    expect(r.dx).toBe(580);
    expect(r.guides).toEqual([{ axis: 'x', at: 640 }]);
  });

  it('does not snap 7 px away with a threshold of 6', () => {
    const r = snapMove(MOVING, [TARGET], CANVAS, 183, 0, 6);
    expect(r).toEqual({ dx: 183, dy: 0, guides: [] });
  });

  it('snaps both axes independently, the nearest candidate winning', () => {
    // Left 196 is 4 px from 200, right 296 is 2 px from the second target's left (298): the right edge wins.
    // Top 298 is 2 px from the first target's top (300).
    const second = { x: 298, y: 400, width: 10, height: 10 };
    const r = snapMove(MOVING, [TARGET, second], CANVAS, 186, 288, 6);
    expect(r.dx).toBe(188);
    expect(r.dy).toBe(290);
    expect(r.guides).toContainEqual({ axis: 'x', at: 298 });
    expect(r.guides).not.toContainEqual({ axis: 'x', at: 200 });
    expect(r.guides).toContainEqual({ axis: 'y', at: 300 });
  });

  it('snaps at exactly the threshold distance', () => {
    // Left 194 is 6 px from 200 (and the centre 244 is 6 px from the target's right, 250).
    const r = snapMove(MOVING, [TARGET], CANVAS, 184, 0, 6);
    expect(r.dx).toBe(190);
    expect(r.guides).toContainEqual({ axis: 'x', at: 200 });
  });

  it('snaps only the y axis when only y is close', () => {
    const r = snapMove(MOVING, [TARGET], CANVAS, 0, 288, 6);
    expect(r).toEqual({ dx: 0, dy: 290, guides: [{ axis: 'y', at: 300 }] });
  });

  it('lists a guide for every target line the snapped rectangle touches', () => {
    // Snapped to x 200..300: the left edge on the first target's left, the right edge on the second's left.
    const second = { x: 300, y: 400, width: 10, height: 10 };
    const r = snapMove(MOVING, [TARGET, second], CANVAS, 187, 0, 6);
    expect(r.dx).toBe(190);
    expect(r.guides).toContainEqual({ axis: 'x', at: 200 });
    expect(r.guides).toContainEqual({ axis: 'x', at: 300 });
  });

  it('snaps a multi-item selection by the edges of its union', () => {
    // Two 20×20 items at x 10 and 90: the union centre (x 60) is no item's edge. Moved by 188 it is at 248, 2 px from
    // the target's centre (250); no single item's edge is within 6 px of a target line.
    const moving = unionRect([{ x: 10, y: 10, width: 20, height: 20 }, { x: 90, y: 10, width: 20, height: 20 }]);
    const target = { x: 245, y: 400, width: 10, height: 10 };
    const r = snapMove(moving, [target], CANVAS, 188, 0, 6);
    expect(r).toEqual({ dx: 190, dy: 0, guides: [{ axis: 'x', at: 250 }] });
  });

  it('never moves further than the threshold, whatever the delta', () => {
    const targets = [TARGET, { x: 298, y: 400, width: 10, height: 10 }, { x: 640, y: 100, width: 333, height: 77 }];
    for (let d = -40; d <= 1300; d += 0.7) {
      const r = snapMove(MOVING, targets, CANVAS, d, d / 3, 6);
      expect(Math.abs(r.dx - d)).toBeLessThanOrEqual(6);
      expect(Math.abs(r.dy - d / 3)).toBeLessThanOrEqual(6);
    }
  });

  it('never moves further than the threshold and lists each guide once', () => {
    const r = snapMove(MOVING, [TARGET, TARGET], CANVAS, 187, 0, 6);
    expect(Math.abs(r.dx - 187)).toBeLessThanOrEqual(6);
    const keys = r.guides.map((g) => `${g.axis}${g.at}`);
    expect(new Set(keys).size).toBe(keys.length);
  });
});

describe('alignMoves', () => {
  const rows: Record<string, Rect> = {
    a: { x: 31, y: 51, width: 100, height: 31 },
    b: { x: 33, y: 86, width: 200, height: 24 },
    c: { x: 36, y: 154, width: 150, height: 31 },
  };

  it('left aligns every left edge to the leftmost', () => {
    expect(alignMoves(rows, 'left')).toEqual({ a: { dx: 0, dy: 0 }, b: { dx: -2, dy: 0 }, c: { dx: -5, dy: 0 } });
  });

  it('right aligns every right edge to the union right', () => {
    // Union right is b's: 233.
    expect(alignMoves(rows, 'right')).toEqual({ a: { dx: 102, dy: 0 }, b: { dx: 0, dy: 0 }, c: { dx: 47, dy: 0 } });
  });

  it('centre aligns horizontal centres to the union centre', () => {
    const r = { a: { x: 0, y: 0, width: 100, height: 10 }, b: { x: 100, y: 50, width: 200, height: 10 } };
    // Union 0..300, centre 150: a's centre 50 → +100, b's centre 200 → −50.
    expect(alignMoves(r, 'centre')).toEqual({ a: { dx: 100, dy: 0 }, b: { dx: -50, dy: 0 } });
  });

  it('top, middle and bottom align on the vertical axis', () => {
    // Union y 51..185, middle 118.
    expect(alignMoves(rows, 'top')).toEqual({ a: { dx: 0, dy: 0 }, b: { dx: 0, dy: -35 }, c: { dx: 0, dy: -103 } });
    expect(alignMoves(rows, 'middle')).toEqual({
      a: { dx: 0, dy: 118 - 66.5 }, b: { dx: 0, dy: 118 - 98 }, c: { dx: 0, dy: 118 - 169.5 },
    });
    expect(alignMoves(rows, 'bottom')).toEqual({ a: { dx: 0, dy: 103 }, b: { dx: 0, dy: 75 }, c: { dx: 0, dy: 0 } });
  });
});

describe('distributeMoves', () => {
  const after = (rects: Record<string, Rect>, moves: Record<string, { dx: number; dy: number }>) =>
    Object.fromEntries(Object.entries(rects).map(([id, r]) => [id, { ...r, x: r.x + moves[id].dx, y: r.y + moves[id].dy }]));

  it('vertically: first and last stay, the middle row moves so both gaps are equal', () => {
    const rows = {
      top: { x: 31, y: 51, width: 100, height: 31 },
      mid: { x: 33, y: 86, width: 100, height: 24 },
      low: { x: 36, y: 154, width: 100, height: 31 },
    };
    const m = distributeMoves(rows, 'y');
    expect(m).toEqual({ top: { dx: 0, dy: 0 }, mid: { dx: 0, dy: 20 }, low: { dx: 0, dy: 0 } });
    const r = after(rows, m);
    expect(r.mid.y - (r.top.y + r.top.height)).toBe(24);
    expect(r.low.y - (r.mid.y + r.mid.height)).toBe(24);
  });

  it('orders by position, not by key order, and keeps the gaps equal horizontally', () => {
    const rects = {
      c: { x: 300, y: 0, width: 50, height: 10 },
      a: { x: 0, y: 5, width: 20, height: 10 },
      d: { x: 90, y: 0, width: 40, height: 10 },
      b: { x: 30, y: 0, width: 10, height: 10 },
    };
    // Span 0..350, sizes 120 → gap (350 − 120) / 3 = 76.67: b at 96.67, d at 183.33.
    const m = distributeMoves(rects, 'x');
    expect(m.a).toEqual({ dx: 0, dy: 0 });
    expect(m.c).toEqual({ dx: 0, dy: 0 });
    expect(m.b.dx).toBeCloseTo(66.667, 2);
    expect(m.d.dx).toBeCloseTo(93.333, 2);
    expect(m.b.dy).toBe(0);
  });

  it('keeps equal (negative) gaps when the rectangles overlap', () => {
    const rects = {
      a: { x: 0, y: 0, width: 100, height: 10 },
      b: { x: 10, y: 0, width: 100, height: 10 },
      c: { x: 50, y: 0, width: 100, height: 10 },
    };
    // Span 0..150, sizes 300 → gap −75: b at 25.
    expect(distributeMoves(rects, 'x').b).toEqual({ dx: 15, dy: 0 });
  });

  it('moves nothing with fewer than three rectangles', () => {
    const m = distributeMoves({ a: { x: 0, y: 0, width: 10, height: 10 }, b: { x: 50, y: 0, width: 10, height: 10 } }, 'x');
    expect(Object.values(m).every((v) => v.dx === 0 && v.dy === 0)).toBe(true);
  });
});

import { describe, it, expect } from 'vitest';
import { snapMove, unionRect } from './align';

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

  it('never moves further than the threshold and lists each guide once', () => {
    const r = snapMove(MOVING, [TARGET, TARGET], CANVAS, 187, 0, 6);
    expect(Math.abs(r.dx - 187)).toBeLessThanOrEqual(6);
    const keys = r.guides.map((g) => `${g.axis}${g.at}`);
    expect(new Set(keys).size).toBe(keys.length);
  });
});

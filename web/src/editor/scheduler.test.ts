import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createRenderScheduler } from './scheduler';

interface Call { arg: string; signal: AbortSignal; resolve: (v: string) => void; reject: (e: unknown) => void }

function setup(opts: { onError?: (e: unknown) => void } = {}) {
  const calls: Call[] = [];
  const results: string[] = [];
  let inFlight = 0;
  let maxInFlight = 0;
  const s = createRenderScheduler<string, string>(
    (arg, signal) => new Promise<string>((resolve, reject) => {
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      calls.push({
        arg, signal,
        resolve: (v) => { inFlight--; resolve(v); },
        reject: (e) => { inFlight--; reject(e); },
      });
    }),
    (r) => results.push(r),
    { delayMs: 150, ...opts },
  );
  return { s, calls, results, maxInFlight: () => maxInFlight };
}

beforeEach(() => { vi.useFakeTimers(); });
afterEach(() => { vi.useRealTimers(); });

describe('render scheduler', () => {
  it('debounces', async () => {
    const { s, calls } = setup();
    s.request('a');
    await vi.advanceTimersByTimeAsync(100);
    s.request('b');
    await vi.advanceTimersByTimeAsync(100);
    s.request('c');
    await vi.advanceTimersByTimeAsync(149);
    expect(calls).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(1);
    expect(calls.map((c) => c.arg)).toEqual(['c']);
  });

  it('never two in flight', async () => {
    const { s, calls, results, maxInFlight } = setup();
    s.request('a');
    await vi.advanceTimersByTimeAsync(150);
    s.request('b');
    await vi.advanceTimersByTimeAsync(150);
    // b is due but a is still running: a is aborted and b waits for it to settle
    expect(calls).toHaveLength(1);
    expect(calls[0].signal.aborted).toBe(true);
    calls[0].resolve('ra');
    await vi.advanceTimersByTimeAsync(0);
    expect(calls.map((c) => c.arg)).toEqual(['a', 'b']);
    calls[1].resolve('rb');
    await vi.advanceTimersByTimeAsync(0);
    expect(maxInFlight()).toBe(1);
    expect(results).toEqual(['rb']);
  });

  it('drops stale results', async () => {
    const { s, calls, results } = setup();
    s.request('a');
    await vi.advanceTimersByTimeAsync(150);
    s.request('b'); // still debouncing: a's result is already stale
    calls[0].resolve('ra');
    await vi.advanceTimersByTimeAsync(0);
    expect(results).toEqual([]);
    await vi.advanceTimersByTimeAsync(150);
    calls[1].resolve('rb');
    await vi.advanceTimersByTimeAsync(0);
    expect(results).toEqual(['rb']);
  });

  it('delivers the result of the latest request', async () => {
    const { s, calls, results } = setup();
    s.request('a');
    await vi.advanceTimersByTimeAsync(150);
    calls[0].resolve('ra');
    await vi.advanceTimersByTimeAsync(0);
    expect(results).toEqual(['ra']);
  });

  it('reports errors of the latest render only, not aborts', async () => {
    const onError = vi.fn();
    const { s, calls } = setup({ onError });
    s.request('a');
    await vi.advanceTimersByTimeAsync(150);
    s.request('b');
    await vi.advanceTimersByTimeAsync(150);
    calls[0].reject(new DOMException('aborted', 'AbortError'));
    await vi.advanceTimersByTimeAsync(0);
    expect(onError).not.toHaveBeenCalled();
    calls[1].reject(new Error('boom'));
    await vi.advanceTimersByTimeAsync(0);
    expect(onError).toHaveBeenCalledTimes(1);
  });

  it('dispose aborts the in-flight render, cancels the timer and drops results', async () => {
    const { s, calls, results } = setup();
    s.request('a');
    await vi.advanceTimersByTimeAsync(150);
    s.request('b');
    s.dispose();
    expect(calls[0].signal.aborted).toBe(true);
    calls[0].resolve('ra');
    await vi.advanceTimersByTimeAsync(1000);
    expect(calls).toHaveLength(1);
    expect(results).toEqual([]);
    s.request('c');
    await vi.advanceTimersByTimeAsync(1000);
    expect(calls).toHaveLength(1);
  });
});

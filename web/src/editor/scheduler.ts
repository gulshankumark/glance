export interface RenderScheduler<T> {
  /** Debounced; when it comes due it aborts a render in flight (edits pre-empt). */
  request(t: T): void;
  /** Renders `t` now unless a render is in flight or a request is waiting: never pre-empts (periodic refreshes). */
  refresh(t: T): void;
  dispose(): void;
}

/**
 * Debounced, single-flight render requests. Only the latest request matters: a request that comes due while a
 * render is in flight aborts it (its result is dropped) and runs once the old one has settled, so at most one render
 * is ever in flight. `onError` receives failures of the latest render only (aborts are silent).
 */
export function createRenderScheduler<T, R>(
  renderFn: (t: T, signal: AbortSignal) => Promise<R>,
  onResult: (r: R) => void,
  { delayMs = 150, onError }: { delayMs?: number; onError?: (e: unknown) => void } = {},
): RenderScheduler<T> {
  let latest: { value: T } | null = null;
  let seq = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let waiting = false; // a request's debounce is running
  let current: AbortController | null = null;
  let disposed = false;
  let due = false; // the debounce elapsed while a render was still in flight

  const start = () => {
    if (disposed || !latest) return;
    due = false;
    const mySeq = seq;
    const ctrl = new AbortController();
    current = ctrl;
    let render: Promise<R>;
    try {
      render = renderFn(latest.value, ctrl.signal);
    } catch (e) {
      render = Promise.reject(e); // a synchronous throw must not leave `current` set forever
    }
    render
      .then(
        (r) => { if (!disposed && mySeq === seq) onResult(r); },
        (e) => { if (!disposed && mySeq === seq && !ctrl.signal.aborted) onError?.(e); },
      )
      .finally(() => {
        if (current === ctrl) current = null;
        if (due) start();
      });
  };

  return {
    request(t) {
      if (disposed) return;
      latest = { value: t };
      seq++;
      due = false; // a newer request restarts the debounce; a settling render must not start before it elapses
      clearTimeout(timer);
      waiting = true;
      timer = setTimeout(() => {
        waiting = false;
        if (current) { due = true; current.abort(); } else start();
      }, delayMs);
    },
    refresh(t) {
      if (disposed || current || waiting) return;
      latest = { value: t };
      seq++;
      start();
    },
    dispose() {
      disposed = true;
      clearTimeout(timer);
      current?.abort();
    },
  };
}

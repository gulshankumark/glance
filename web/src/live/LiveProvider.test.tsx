import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { LiveProvider, useLiveStale, useLiveStatus, useSensorValues } from './LiveProvider';
import { resetUnauthorizedHandler, setUnauthorizedHandler } from '../api/client';

class FakeEventSource extends EventTarget {
  static instances: FakeEventSource[] = [];
  onopen: ((ev: Event) => void) | null = null;
  onerror: ((ev: Event) => void) | null = null;
  closed = false;

  constructor(public url: string) {
    super();
    FakeEventSource.instances.push(this);
  }

  close() {
    this.closed = true;
  }

  open() {
    this.onopen?.(new Event('open'));
  }

  fail() {
    this.onerror?.(new Event('error'));
  }

  emit(type: string, data: unknown) {
    this.dispatchEvent(new MessageEvent(type, { data: JSON.stringify(data) }));
  }
}

const latest = () => FakeEventSource.instances[FakeEventSource.instances.length - 1];

const Probe = () => {
  const status = useLiveStatus();
  const stale = useLiveStale();
  const values = useSensorValues();
  return <div data-testid="probe" data-status={status} data-stale={String(stale)} data-cpu={String(values['cpu/t'])} />;
};

const probe = () => screen.getByTestId('probe');

let authState = { loopback: true, authenticated: false, networkAccess: false };

beforeEach(() => {
  vi.useFakeTimers();
  FakeEventSource.instances = [];
  vi.stubGlobal('EventSource', FakeEventSource);
  authState = { loopback: true, authenticated: false, networkAccess: false };
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(authState), { headers: { 'content-type': 'application/json' } })),
  );
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  resetUnauthorizedHandler();
});

const mount = () =>
  render(
    <LiveProvider>
      <Probe />
    </LiveProvider>,
  );

describe('LiveProvider', () => {
  it('starts connecting and opens only on the stream open event', () => {
    mount();
    expect(probe().dataset.status).toBe('connecting');

    // An event before open does not promote the status (a real EventSource fires open first).
    act(() => latest().emit('sensors', { 'cpu/t': 50 }));
    expect(probe().dataset.status).toBe('connecting');

    act(() => latest().open());
    expect(probe().dataset.status).toBe('open');
  });

  it('treats values as stale until the first sensors event after opening', () => {
    mount();
    act(() => latest().open());
    expect(probe().dataset.stale).toBe('true');

    act(() => latest().emit('sensors', { 'cpu/t': 55 }));
    expect(probe().dataset.stale).toBe('false');
    expect(probe().dataset.cpu).toBe('55');
  });

  it('greys values while reconnecting and after reconnecting until the next sensors event', async () => {
    mount();
    act(() => latest().open());
    act(() => latest().emit('sensors', { 'cpu/t': 55 }));

    await act(async () => latest().fail());
    expect(probe().dataset.status).toBe('reconnecting');
    expect(probe().dataset.stale).toBe('true');

    await act(async () => vi.advanceTimersByTime(1000));
    expect(FakeEventSource.instances).toHaveLength(2);
    act(() => latest().open());
    expect(probe().dataset.status).toBe('open');
    expect(probe().dataset.stale).toBe('true');

    act(() => latest().emit('sensors', { 'cpu/t': 57 }));
    expect(probe().dataset.stale).toBe('false');
  });

  it('stays "connecting" while the first connection keeps failing, backing off 1, 2, 5 s', async () => {
    mount();
    await act(async () => latest().fail());
    expect(probe().dataset.status).toBe('connecting');

    await act(async () => vi.advanceTimersByTime(999));
    expect(FakeEventSource.instances).toHaveLength(1);
    await act(async () => vi.advanceTimersByTime(1));
    expect(FakeEventSource.instances).toHaveLength(2);

    await act(async () => latest().fail());
    await act(async () => vi.advanceTimersByTime(2000));
    expect(FakeEventSource.instances).toHaveLength(3);

    await act(async () => latest().fail());
    await act(async () => vi.advanceTimersByTime(4999));
    expect(FakeEventSource.instances).toHaveLength(3);
    await act(async () => vi.advanceTimersByTime(1));
    expect(FakeEventSource.instances).toHaveLength(4);
    expect(probe().dataset.status).toBe('connecting');
  });

  it('sends a remote caller without a login to the login page instead of retrying forever', async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    authState = { loopback: false, authenticated: false, networkAccess: true };
    mount();

    await act(async () => latest().fail());

    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    await act(async () => vi.advanceTimersByTime(10000));
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('keeps retrying when the caller is allowed (loopback)', async () => {
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);
    mount();

    await act(async () => latest().fail());
    await act(async () => vi.advanceTimersByTime(1000));

    expect(onUnauthorized).not.toHaveBeenCalled();
    expect(FakeEventSource.instances).toHaveLength(2);
  });
});

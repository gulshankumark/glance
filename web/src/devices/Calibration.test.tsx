import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import type { DeviceView } from '../api/types';
import { Calibration } from './Calibration';

const device: DeviceView = {
  key: 'dev 1', driverId: 'd', name: 'n', status: 'connected', width: 1280, height: 480, modelCode: 'm', serial: 's',
  message: null, nextRetry: null, rotation: 0, activeLayout: null, layoutMismatch: false, fps: 0, framesSent: 0,
  gamma: 1, brightness: 100,
};

interface Call {
  method: string;
  url: string;
  body: unknown;
}

let calls: Call[];
/** Answers a request; the default echoes a PUT body merged into the device. */
let answer: (call: Call) => Response | Promise<Response>;

const json = (status: number, data: unknown) =>
  new Response(JSON.stringify(data), { status, headers: { 'content-type': 'application/json' } });

beforeEach(() => {
  vi.useFakeTimers();
  calls = [];
  answer = (call) =>
    call.method === 'PUT' ? json(200, { ...device, ...(call.body as object) }) : new Response(null, { status: 204 });
  vi.stubGlobal('fetch', async (url: string, init: RequestInit) => {
    const call = { method: init.method ?? 'GET', url, body: init.body ? JSON.parse(String(init.body)) : undefined };
    calls.push(call);
    return answer(call);
  });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

/** Lets pending fetch promises and their state updates settle. */
const flush = () => act(async () => {
  await vi.advanceTimersByTimeAsync(0);
});

const gamma = () => screen.getByLabelText('Gamma') as HTMLInputElement;
const brightness = () => screen.getByLabelText('Brightness') as HTMLInputElement;
const puts = () => calls.filter((c) => c.method === 'PUT');

describe('Calibration', () => {
  it('sends one PUT with the last value after quick slider moves', async () => {
    const onUpdated = vi.fn();
    render(<Calibration device={device} onUpdated={onUpdated} />);

    fireEvent.change(gamma(), { target: { value: '1.2' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(100); });
    fireEvent.change(gamma(), { target: { value: '1.6' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(100); });
    fireEvent.change(gamma(), { target: { value: '2.25' } });
    expect(screen.getByText('2.25')).toBeTruthy();
    expect(puts()).toHaveLength(0);

    await act(async () => { await vi.advanceTimersByTimeAsync(150); });
    await flush();

    expect(puts()).toEqual([{ method: 'PUT', url: '/api/devices/dev%201', body: { gamma: 2.25, brightness: 100 } }]);
    expect(onUpdated).toHaveBeenCalledWith({ ...device, gamma: 2.25 });
  });

  it('shows the brightness in %', () => {
    render(<Calibration device={{ ...device, brightness: 80 }} onUpdated={() => undefined} />);
    expect(brightness().value).toBe('80');
    expect(screen.getByText('80%')).toBeTruthy();
    expect(screen.getByText('1.00')).toBeTruthy();
  });

  it('Reset sends gamma 1 and brightness 100', async () => {
    render(<Calibration device={{ ...device, gamma: 2.2, brightness: 70 }} onUpdated={() => undefined} />);
    fireEvent.click(screen.getByRole('button', { name: 'Reset' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(150); });
    await flush();

    expect(puts().map((c) => c.body)).toEqual([{ gamma: 1, brightness: 100 }]);
    expect(gamma().value).toBe('1');
    expect(brightness().value).toBe('100');
  });

  it('a 400 shows the server message and restores the saved values', async () => {
    answer = () => json(400, { error: 'gamma must be 0.5..3' });
    const onUpdated = vi.fn();
    render(<Calibration device={{ ...device, gamma: 1.5 }} onUpdated={onUpdated} />);

    fireEvent.change(gamma(), { target: { value: '2.5' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(150); });
    await flush();

    expect(screen.getByRole('alert').textContent).toBe('gamma must be 0.5..3');
    expect(gamma().value).toBe('1.5');
    expect(onUpdated).not.toHaveBeenCalled();
  });

  it('ignores an answer that arrives after a newer one was sent', async () => {
    const resolvers: Array<(r: Response) => void> = [];
    answer = () => new Promise<Response>((resolve) => resolvers.push(resolve));
    const onUpdated = vi.fn();
    render(<Calibration device={device} onUpdated={onUpdated} />);

    fireEvent.change(gamma(), { target: { value: '1.5' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(150); });
    fireEvent.change(gamma(), { target: { value: '2' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(150); });
    expect(puts()).toHaveLength(2);

    resolvers[1](json(200, { ...device, gamma: 2 }));
    await flush();
    resolvers[0](json(400, { error: 'too late' }));
    await flush();

    expect(onUpdated).toHaveBeenCalledTimes(1);
    expect(onUpdated).toHaveBeenCalledWith({ ...device, gamma: 2 });
    expect(screen.queryByRole('alert')).toBeNull();
    expect(gamma().value).toBe('2');
  });

  it('the test pattern toggle POSTs, then DELETEs', async () => {
    render(<Calibration device={device} onUpdated={() => undefined} />);
    const toggle = screen.getByRole('button', { name: 'Show test pattern' });
    expect(toggle.getAttribute('aria-pressed')).toBe('false');

    fireEvent.click(toggle);
    await flush();
    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual(['POST /api/devices/dev%201/test-pattern']);
    expect(toggle.getAttribute('aria-pressed')).toBe('true');

    fireEvent.click(toggle);
    await flush();
    expect(calls.map((c) => c.method)).toEqual(['POST', 'DELETE']);
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
  });

  it('the test pattern toggle turns itself off after 120 s', async () => {
    render(<Calibration device={device} onUpdated={() => undefined} />);
    const toggle = screen.getByRole('button', { name: 'Show test pattern' });
    fireEvent.click(toggle);
    await flush();

    await act(async () => { await vi.advanceTimersByTimeAsync(119_000); });
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    await act(async () => { await vi.advanceTimersByTimeAsync(1_000); });
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    // the service ends it by itself: no DELETE needed
    expect(calls.map((c) => c.method)).toEqual(['POST']);
  });

  it('DELETEs the test pattern on unmount while it is on', async () => {
    const { unmount } = render(<Calibration device={device} onUpdated={() => undefined} />);
    fireEvent.click(screen.getByRole('button', { name: 'Show test pattern' }));
    await flush();
    unmount();
    await flush();
    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual([
      'POST /api/devices/dev%201/test-pattern',
      'DELETE /api/devices/dev%201/test-pattern',
    ]);
  });

  it('a failed POST shows the error and keeps the toggle off', async () => {
    answer = () => json(404, { error: 'unknown device' });
    render(<Calibration device={device} onUpdated={() => undefined} />);
    fireEvent.click(screen.getByRole('button', { name: 'Show test pattern' }));
    await flush();
    expect(screen.getByRole('alert').textContent).toBe('unknown device');
    expect(screen.getByRole('button', { name: 'Show test pattern' }).getAttribute('aria-pressed')).toBe('false');
  });

  it('shows the hint next to 16 grey steps from black to white', () => {
    render(<Calibration device={device} onUpdated={() => undefined} />);
    expect(screen.getByText('Raise gamma until the dark grey steps on the screen look like they do here.')).toBeTruthy();
    const steps = screen.getByTestId('grey-steps').children;
    expect(steps).toHaveLength(16);
    expect((steps[0] as HTMLElement).style.backgroundColor).toBe('rgb(0, 0, 0)');
    expect((steps[1] as HTMLElement).style.backgroundColor).toBe('rgb(17, 17, 17)');
    expect((steps[15] as HTMLElement).style.backgroundColor).toBe('rgb(255, 255, 255)');
  });
});

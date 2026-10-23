import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LiveStateProvider, type LiveContextValue } from './LiveProvider';
import { LiveValue } from './LiveValue';

const renderWith = (value: Partial<LiveContextValue>) =>
  render(
    <LiveStateProvider value={{ status: 'open', stale: false, sensorValues: {}, devices: null, ...value }}>
      <LiveValue>61 °C</LiveValue>
    </LiveStateProvider>,
  );

describe('LiveValue', () => {
  it('renders a fresh value normally', () => {
    renderWith({});
    const el = screen.getByText('61 °C');
    expect(el.getAttribute('aria-busy')).toBeNull();
  });

  it('greys a stale value and marks it busy', () => {
    renderWith({ status: 'reconnecting', stale: true });
    const el = screen.getByText('61 °C');
    expect(el.getAttribute('aria-busy')).toBe('true');
    expect(el.style.opacity).not.toBe('');
  });
});

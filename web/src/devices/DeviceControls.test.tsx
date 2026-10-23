import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { DeviceView } from '../api/types';
import { LayoutSelect } from './DeviceControls';

const device: DeviceView = {
  key: 'k', driverId: 'd', name: 'n', status: 'connected', width: 480, height: 480, modelCode: 'm', serial: 's',
  message: null, nextRetry: null, rotation: 0, activeLayout: 'Gaming', layoutMismatch: false, fps: 0, framesSent: 0,
  gamma: 1, brightness: 100,
};

describe('LayoutSelect', () => {
  it('shows the active layout while the layout list is not loaded', () => {
    render(<LayoutSelect device={device} layouts={[]} onChange={() => undefined} />);
    const select = screen.getByLabelText('Active layout') as HTMLSelectElement;
    expect(select.value).toBe('Gaming');
    expect(select.selectedOptions[0].textContent).toBe('Gaming');
  });

  it('lists the active layout once when the list has it', () => {
    const layouts = [{ name: 'Gaming', width: 480, height: 480, error: null }];
    render(<LayoutSelect device={device} layouts={layouts} onChange={() => undefined} />);
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual([
      'None (built-in status screen)',
      'Gaming (480×480)',
    ]);
  });
});

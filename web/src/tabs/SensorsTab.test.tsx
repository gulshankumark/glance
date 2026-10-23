import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { DiagnosticBanners } from './SensorsTab';

afterEach(cleanup);

describe('DiagnosticBanners', () => {
  it('shows every diagnostic as a status message, also when codes repeat', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    try {
      render(
        <DiagnosticBanners
          diagnostics={[
            { code: 'driver', message: 'First problem' },
            { code: 'driver', message: 'Second problem' },
          ]}
        />,
      );
      expect(screen.getAllByRole('status').map((s) => s.textContent)).toEqual([
        expect.stringContaining('First problem'),
        expect.stringContaining('Second problem'),
      ]);
      expect(error.mock.calls.flat().join(' ')).not.toMatch(/same key/);
    } finally {
      error.mockRestore();
    }
  });
});

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { NoticeBanner } from './NoticeBanner';

let calls: { method: string; url: string }[];

beforeEach(() => {
  calls = [];
  vi.stubGlobal('fetch', async (url: string, init: RequestInit = {}) => {
    calls.push({ method: init.method ?? 'GET', url });
    if ((init.method ?? 'GET') === 'GET') {
      return new Response(
        JSON.stringify([
          { id: 'a', severity: 'warning', text: 'First <b>notice</b>' },
          { id: 'b', severity: 'error', text: 'Second notice' },
        ]),
        { status: 200, headers: { 'content-type': 'application/json' } },
      );
    }
    return new Response(null, { status: 204 });
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('NoticeBanner', () => {
  it('renders one banner per notice, as text', async () => {
    render(<NoticeBanner />);
    expect(await screen.findByText('First <b>notice</b>')).toBeTruthy();
    expect(screen.getByText('Second notice')).toBeTruthy();
    expect(screen.getAllByRole('button', { name: 'Dismiss' })).toHaveLength(2);
  });

  it('dismisses a notice and removes it', async () => {
    render(<NoticeBanner />);
    await screen.findByText('Second notice');
    fireEvent.click(screen.getAllByRole('button', { name: 'Dismiss' })[1]);
    await waitFor(() => expect(screen.queryByText('Second notice')).toBeNull());
    expect(calls).toContainEqual({ method: 'POST', url: '/api/notices/b/dismiss' });
    expect(screen.getByText('First <b>notice</b>')).toBeTruthy();
  });
});

import { afterEach, describe, expect, it } from 'vitest';
import { clearStash, readStash, writeStash } from './draftStash';
import { newLayout } from './model';

describe('draft stash', () => {
  afterEach(() => sessionStorage.clear());

  it('round-trips the draft of a stored layout and of a new one', () => {
    const draft = newLayout('A', 10, 20);
    writeStash({ layoutName: 'A', draft, savedVersion: '{"x":1}' });
    expect(readStash()).toEqual({ layoutName: 'A', draft, savedVersion: '{"x":1}' });

    writeStash({ layoutName: null, draft, savedVersion: '' });
    expect(readStash()).toEqual({ layoutName: null, draft, savedVersion: '' });
  });

  it('clears, and ignores missing or malformed entries', () => {
    writeStash({ layoutName: 'A', draft: newLayout('A', 1, 1), savedVersion: '' });
    clearStash();
    expect(readStash()).toBeNull();

    sessionStorage.setItem('glance.layoutDraft', '{not json');
    expect(readStash()).toBeNull();
    sessionStorage.setItem('glance.layoutDraft', JSON.stringify({ layoutName: 3, draft: {}, savedVersion: '' }));
    expect(readStash()).toBeNull();
    sessionStorage.setItem('glance.layoutDraft', JSON.stringify({ layoutName: 'A', draft: { name: 'A' }, savedVersion: '' }));
    expect(readStash()).toBeNull();
  });
});

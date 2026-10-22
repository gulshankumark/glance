import { describe, expect, it } from 'vitest';
import { returnHashFrom } from './returnHash';

describe('returnHashFrom', () => {
  it('returns the decoded return hash', () => {
    expect(returnHashFrom('#login?return=%23sensors')).toBe('#sensors');
    expect(returnHashFrom('#login?return=%23layouts%2FMy%20Layout')).toBe('#layouts/My Layout');
  });

  it('decodes only once', () => {
    // "%2523x" decodes once to "%23x", which is not a hash, so it is rejected rather than decoded again to "#x".
    expect(returnHashFrom('#login?return=%2523sensors')).toBe('#display');
  });

  it('falls back to #display for a missing, foreign or login return', () => {
    expect(returnHashFrom('#login')).toBe('#display');
    expect(returnHashFrom('#login?return=')).toBe('#display');
    expect(returnHashFrom('#login?return=https%3A%2F%2Fevil.example')).toBe('#display');
    expect(returnHashFrom('#login?return=%23login%3Freturn%3D%2523sensors')).toBe('#display');
  });
});

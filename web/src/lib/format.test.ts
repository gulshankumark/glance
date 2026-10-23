import { describe, it, expect } from 'vitest';
import { formatNextRetry, formatValue } from './format';

describe('formatValue', () => {
  it('formatValue null → "—"', () => {
    expect(formatValue(null)).toBe('—');
    expect(formatValue(undefined)).toBe('—');
    expect(formatValue(Number.NaN)).toBe('—');
    expect(formatValue(Number.POSITIVE_INFINITY)).toBe('—');
  });

  it('formatValue rounds', () => {
    expect(formatValue(61.4)).toBe('61');
    expect(formatValue(61.6)).toBe('62');
    expect(formatValue(61.456, '°C', 1)).toBe('61.5 °C');
    expect(formatValue(1234.5, 'MHz', 0)).toBe('1235 MHz');
    expect(formatValue(50, '%', 0)).toBe('50 %');
  });
});

describe('formatNextRetry', () => {
  it('is null for absent or invalid times, never "Invalid Date"', () => {
    expect(formatNextRetry(null)).toBeNull();
    expect(formatNextRetry('')).toBeNull();
    expect(formatNextRetry('not a date')).toBeNull();
  });

  it('formats a valid ISO time', () => {
    expect(formatNextRetry('2026-10-23T10:15:30Z')).toMatch(/\d/);
  });
});

import type { Threshold } from '../api/types';
import type { Field } from './schema';

/**
 * The value a number field takes from what was typed: clamped to the field's bounds (whole numbers for bounded
 * fields), `null` for an empty unbounded field (a gauge's automatic minimum/maximum), `undefined` when the text is
 * not usable (the field keeps its old value).
 */
export function parseNumber(field: Field, raw: string): number | null | undefined {
  const text = raw.trim();
  const bounded = field.min !== undefined || field.max !== undefined;
  if (text === '') return bounded ? undefined : null;
  let v = Number(text);
  if (!Number.isFinite(v)) return undefined;
  if (bounded && Number.isInteger(field.step ?? 1)) v = Math.round(v);
  if (field.min !== undefined) v = Math.max(field.min, v);
  if (field.max !== undefined) v = Math.min(field.max, v);
  return v;
}

/** `#RRGGBB` or `#AARRGGBB` (docs/api.md). */
export const isColor = (s: string): boolean => /^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$/.test(s);

/** `current` with its colour replaced by `rgb` (#RRGGBB from a colour picker), keeping any alpha. */
export const withRgb = (current: string, rgb: string): string =>
  current.length === 9 ? `#${current.slice(1, 3)}${rgb.slice(1)}` : rgb;

/** The #RRGGBB part of a colour, for a colour picker. */
export const rgbOf = (color: string): string => (color.length === 9 ? `#${color.slice(3)}` : color);

/** The thresholds with a new row 10 above the last (they stay ascending by `from`). */
export const addThreshold = (list: Threshold[]): Threshold[] => [
  ...list,
  { from: list.length ? list[list.length - 1].from + 10 : 0, color: '#FF4444' },
];

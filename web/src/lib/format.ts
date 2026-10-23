import type { Settings } from '../api/types';

export function formatValue(
  value: number | null | undefined,
  unit?: string,
  decimals = 0
): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return '—';
  }

  const rounded = decimals > 0 ? value.toFixed(decimals) : Math.round(value).toString();
  return unit ? `${rounded} ${unit}` : rounded;
}

/**
 * A sensor value with its unit. Temperatures arrive in the canonical °C (docs/api.md) and are shown in the chosen
 * unit; every other unit is shown as is.
 */
export function formatSensorValue(
  value: number | null | undefined,
  unit: string | null | undefined,
  temperatureUnit: Settings['unit'],
  decimals = 0
): string {
  if (unit === '°C' && temperatureUnit === 'fahrenheit') {
    return formatValue(value === null || value === undefined ? value : (value * 9) / 5 + 32, '°F', decimals);
  }
  return formatValue(value, unit ?? undefined, decimals);
}

/** Local time of an ISO `nextRetry`, or null when it is absent or not a valid date (never "Invalid Date"). */
export function formatNextRetry(iso: string | null): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleTimeString();
}

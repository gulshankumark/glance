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

/** Local time of an ISO `nextRetry`, or null when it is absent or not a valid date (never "Invalid Date"). */
export function formatNextRetry(iso: string | null): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleTimeString();
}

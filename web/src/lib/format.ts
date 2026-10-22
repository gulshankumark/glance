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

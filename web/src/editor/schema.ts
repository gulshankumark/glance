import type { ItemType } from '../api/types';

/**
 * Numeric bounds copied from src/Glance.Core/Layouts/LayoutLimits.cs (and the literal ranges in
 * LayoutValidator.cs); schema.test.ts parses those files so any drift fails the build.
 */
export const LIMITS = {
  position: { min: -4096, max: 8192 },
  itemSize: { min: 1, max: 4096 },
  layoutSize: { min: 1, max: 4096 },
  fontSize: { min: 4, max: 512 },
  fontWeight: { min: 100, max: 900 },
  ticks: { min: 0, max: 100 },
  segments: { min: 2, max: 50 },
  gap: { min: 0, max: 100 },
  thickness: { min: 1, max: 500 },
  startAngle: { min: -360, max: 720 },
  sweepAngle: { min: 1, max: 360 },
  decimals: { min: 0, max: 3 },
  windowSeconds: { min: 1, max: 1800 },
} as const;

export type FieldKind =
  | 'text' | 'number' | 'color' | 'select' | 'sensor' | 'font' | 'timezone' | 'thresholds' | 'bool' | 'asset';

export interface Field {
  /** Item property the field edits ('font' edits the whole FontSpec; 'thresholds' the list). */
  key: string;
  label: string;
  kind: FieldKind;
  min?: number;
  max?: number;
  step?: number;
  options?: Array<{ value: string; label: string }>;
}

const num = (key: string, label: string, b: { min: number; max: number }, step = 1): Field =>
  ({ key, label, kind: 'number', min: b.min, max: b.max, step });

const position: Field[] = [num('x', 'X', LIMITS.position), num('y', 'Y', LIMITS.position)];
const size: Field[] = [num('width', 'Width', LIMITS.itemSize), num('height', 'Height', LIMITS.itemSize)];
const align: Field = {
  key: 'align', label: 'Align', kind: 'select',
  options: [{ value: 'left', label: 'Left' }, { value: 'center', label: 'Center' }, { value: 'right', label: 'Right' }],
};
const textStyle: Field[] = [
  { key: 'font', label: 'Font', kind: 'font' },
  { key: 'color', label: 'Colour', kind: 'color' },
  align,
];
const sensor: Field = { key: 'sensor', label: 'Sensor', kind: 'sensor' };
const label: Field = { key: 'label', label: 'Label', kind: 'text' };
const vertical: Field = { key: 'vertical', label: 'Vertical', kind: 'bool' };
const arcAngles: Field[] = [
  num('startAngle', 'Start angle', LIMITS.startAngle),
  num('sweepAngle', 'Sweep angle', LIMITS.sweepAngle),
];
const windowSeconds: Field = num('windowSeconds', 'Window (seconds)', LIMITS.windowSeconds);

/** Common gauge fields; min/max are left empty for automatic scaling. */
const gauge: Field[] = [
  sensor, ...size,
  { key: 'min', label: 'Minimum', kind: 'number', step: 1 },
  { key: 'max', label: 'Maximum', kind: 'number', step: 1 },
  { key: 'color', label: 'Colour', kind: 'color' },
  { key: 'trackColor', label: 'Track colour', kind: 'color' },
  { key: 'thresholds', label: 'Thresholds', kind: 'thresholds' },
  { key: 'showValue', label: 'Show value', kind: 'bool' },
  label,
  { key: 'font', label: 'Font', kind: 'font' },
];

const FIELDS: Record<ItemType, Field[]> = {
  text: [{ key: 'text', label: 'Text', kind: 'text' }, ...position, ...textStyle],
  sensor: [
    sensor, label, ...position,
    { key: 'showUnit', label: 'Show unit', kind: 'bool' },
    num('decimals', 'Decimals', LIMITS.decimals),
    ...textStyle,
  ],
  clock: [
    { key: 'format', label: 'Format', kind: 'text' },
    { key: 'timeZone', label: 'Time zone', kind: 'timezone' },
    label, ...position, ...textStyle,
  ],
  image: [{ key: 'assetId', label: 'Image', kind: 'asset' }, ...position, ...size],
  bar: [...position, ...gauge, vertical, { key: 'rounded', label: 'Rounded', kind: 'bool' }],
  segmentedBar: [
    ...position, ...gauge, vertical,
    num('segments', 'Segments', LIMITS.segments), num('gap', 'Gap', LIMITS.gap),
  ],
  arcGauge: [...position, ...gauge, ...arcAngles, num('thickness', 'Thickness', LIMITS.thickness)],
  dial: [
    ...position, ...gauge, ...arcAngles,
    num('ticks', 'Ticks', LIMITS.ticks),
    { key: 'needleColor', label: 'Needle colour', kind: 'color' },
  ],
  historyGraph: [
    ...position, ...gauge, windowSeconds,
    { key: 'area', label: 'Fill area', kind: 'bool' },
    { key: 'grid', label: 'Grid', kind: 'bool' },
  ],
  sparkline: [...position, ...gauge, windowSeconds],
};

/** The editable properties of an item type, in display order. */
export function fieldsFor(type: ItemType): Field[] {
  return FIELDS[type];
}

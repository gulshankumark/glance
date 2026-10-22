import type { Item, ItemType, Sensor, SensorsResponse } from '../api/types';

const ROLE_LABELS: Record<string, string> = {
  'cpu.temperature': 'CPU temperature',
  'cpu.load': 'CPU load',
  'cpu.clock': 'CPU clock',
  'cpu.power.package': 'CPU package power',
  'cpu.power.cores': 'CPU cores power',
  'cpu.fan': 'CPU fan',
  'gpu.temperature': 'GPU temperature',
  'gpu.load': 'GPU load',
  'gpu.clock': 'GPU clock',
  'gpu.memory.clock': 'GPU memory clock',
  'gpu.memory.load': 'GPU memory load',
  'gpu.fan': 'GPU fan',
  'gpu.power': 'GPU power',
  'memory.load': 'Memory load',
  'memory.used': 'Memory used',
};

export const ROLE_PREFIX = 'role:';

/** A role ("cpu.temperature") in plain words; unknown roles as they are. */
export const roleLabel = (role: string): string => ROLE_LABELS[role] ?? role;

const HARDWARE_GROUPS: Array<{ kind: Sensor['hardwareKind']; label: string }> = [
  { kind: 'cpu', label: 'CPU' },
  { kind: 'gpu', label: 'GPU' },
  { kind: 'memory', label: 'Memory' },
  { kind: 'storage', label: 'Storage' },
  { kind: 'motherboard', label: 'Motherboard' },
  { kind: 'other', label: 'Other' },
];

/** The name a user knows a sensor by: its alias, else its own name. */
export const displayName = (s: Sensor): string => s.alias || s.name;

/** The name of a sensor reference (sensor id or "role:<role>"); the ref itself when the sensor is unknown. */
export function sensorName(ref: string, catalog: SensorsResponse | null): string {
  if (ref.startsWith(ROLE_PREFIX)) return roleLabel(ref.slice(ROLE_PREFIX.length));
  const sensor = catalog?.sensors.find((s) => s.id === ref);
  return sensor ? displayName(sensor) : ref;
}

/** Sensors grouped by hardware kind (CPU, GPU, …); empty groups are left out. */
export function sensorGroups(catalog: SensorsResponse): Array<{ label: string; sensors: Sensor[] }> {
  return HARDWARE_GROUPS.map((g) => ({
    label: g.label,
    sensors: catalog.sensors.filter((s) => s.hardwareKind === g.kind),
  })).filter((g) => g.sensors.length > 0);
}

export interface SensorOption {
  /** Sensor id or "role:<role>". */
  value: string;
  label: string;
}

/** Every binding a sensor field can take: the roles first, then each sensor. */
export function sensorOptions(catalog: SensorsResponse | null): SensorOption[] {
  if (!catalog) return [];
  return [
    ...Object.keys(catalog.roles).map((role) => ({ value: ROLE_PREFIX + role, label: `Role: ${roleLabel(role)}` })),
    ...catalog.sensors.map((s) => ({ value: s.id, label: `${displayName(s)} — ${s.hardware}` })),
  ];
}

/** True when every word of `query` occurs in `text` (case-insensitive); an empty query matches everything. */
export function matches(text: string, query: string): boolean {
  const hay = text.toLowerCase();
  return query
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((w) => hay.includes(w));
}

/** dataTransfer type of a sensor dragged from the sensor tree; the data is JSON `{ref, label}`. */
export const SENSOR_DRAG_TYPE = 'application/x-glance-sensor';

export const ITEM_LABELS: Record<ItemType, string> = {
  text: 'Text',
  sensor: 'Sensor',
  clock: 'Clock',
  image: 'Image',
  bar: 'Bar',
  segmentedBar: 'Segmented bar',
  arcGauge: 'Arc gauge',
  dial: 'Dial',
  historyGraph: 'History graph',
  sparkline: 'Sparkline',
};

/** "Sensor — CPU temperature", "Text — Hello": the item's kind and what it shows (never its id). */
export function describeItem(item: Item, catalog: SensorsResponse | null): string {
  let what: string;
  if ('sensor' in item) what = sensorName(item.sensor, catalog);
  else if (item.type === 'text') what = item.text || '(empty)';
  else if (item.type === 'clock') what = item.label || (item.timeZone === 'local' ? 'Local time' : item.timeZone);
  else what = `${item.width}×${item.height}`;
  return `${ITEM_LABELS[item.type]} — ${what}`;
}

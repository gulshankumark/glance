import React, { useMemo, useState } from 'react';
import type { SensorsResponse } from '../api/types';
import { formatValue } from '../lib/format';
import { useSensorValues } from '../live/LiveProvider';
import { LiveValue } from '../live/LiveValue';
import { displayName, matches, roleLabel, ROLE_PREFIX, SENSOR_DRAG_TYPE, sensorGroups } from './catalog';
import styles from './Editor.module.css';

export interface SensorTreeProps {
  catalog: SensorsResponse | null;
  /** Click (or Enter) on a sensor: add it without dragging (touch screens, keyboard). */
  onAdd: (ref: string, label: string) => void;
}

interface Leaf {
  ref: string;
  label: string;
  /** Text matched by the search box. */
  search: string;
  detail: string | null;
  sensorId: string | null;
  unit: string | null;
  initial: number | null;
}

/** The tree's groups and leaves filtered by `query`; rebuilt only when the catalog or the query change. */
export function useTreeGroups(catalog: SensorsResponse | null, query: string): Array<{ label: string; leaves: Leaf[] }> {
  return useMemo(() => {
    if (!catalog) return [];
    const byId = new Map(catalog.sensors.map((s) => [s.id, s]));
    const roles: Leaf[] = Object.entries(catalog.roles).map(([role, id]) => {
      const target = id ? byId.get(id) : undefined;
      return {
        ref: ROLE_PREFIX + role,
        label: roleLabel(role),
        search: `${roleLabel(role)} ${role}`,
        detail: target ? displayName(target) : 'not available',
        sensorId: target?.id ?? null,
        unit: target?.unit ?? null,
        initial: target?.value ?? null,
      };
    });
    return [
      { label: 'Roles', leaves: roles },
      ...sensorGroups(catalog).map((g) => ({
        label: g.label,
        leaves: g.sensors.map<Leaf>((s) => ({
          ref: s.id,
          label: displayName(s),
          search: `${displayName(s)} ${s.name} ${s.hardware}`,
          detail: s.alias ? s.name : null,
          sensorId: s.id,
          unit: s.unit,
          initial: s.value,
        })),
      })),
    ]
      .map((g) => ({ ...g, leaves: g.leaves.filter((l) => matches(l.search, query)) }))
      .filter((g) => g.leaves.length > 0);
  }, [catalog, query]);
}

/**
 * Sensors grouped by hardware kind plus a "Roles" group, with live values and a search box. Each sensor can be
 * dragged onto the canvas (dataTransfer SENSOR_DRAG_TYPE) or clicked to add it.
 */
export const SensorTree: React.FC<SensorTreeProps> = ({ catalog, onAdd }) => {
  const [query, setQuery] = useState('');
  const values = useSensorValues();

  const groups = useTreeGroups(catalog, query);
  if (!catalog) return <p className={styles.muted}>Loading sensors…</p>;

  const valueOf = (leaf: Leaf) => {
    if (!leaf.sensorId) return null;
    return leaf.sensorId in values ? values[leaf.sensorId] : leaf.initial;
  };

  return (
    <div data-testid="sensor-tree">
      <input
        type="search"
        className={styles.input}
        aria-label="Search sensors"
        placeholder="Search sensors"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />
      <p className={styles.hint}>Drag a sensor onto the canvas, or click it to add it in the middle.</p>
      {groups.length === 0 && <p className={styles.muted}>No sensor matches.</p>}
      {groups.map((g) => (
        <details key={g.label} open className={styles.group}>
          <summary>{g.label}</summary>
          <ul className={styles.leaves}>
            {g.leaves.map((leaf) => (
              <li key={leaf.ref}>
                <button
                  type="button"
                  draggable
                  data-testid={`sensor-leaf-${leaf.ref}`}
                  className={styles.leaf}
                  onDragStart={(e) => {
                    e.dataTransfer.setData(SENSOR_DRAG_TYPE, JSON.stringify({ ref: leaf.ref, label: leaf.label }));
                    e.dataTransfer.effectAllowed = 'copy';
                  }}
                  onClick={() => onAdd(leaf.ref, leaf.label)}
                >
                  <span className={styles.leafName}>
                    {leaf.label}
                    {leaf.detail && <span className={styles.leafDetail}>{leaf.detail}</span>}
                  </span>
                  {leaf.sensorId && (
                    <LiveValue className={styles.leafValue}>{formatValue(valueOf(leaf), leaf.unit ?? undefined)}</LiveValue>
                  )}
                </button>
              </li>
            ))}
          </ul>
        </details>
      ))}
    </div>
  );
};

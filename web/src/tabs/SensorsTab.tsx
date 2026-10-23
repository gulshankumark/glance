import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { Sensor, SensorsResponse } from '../api/types';
import { displayName, roleLabel } from '../editor/catalog';
import { useTreeGroups } from '../editor/SensorTree';
import { formatValue } from '../lib/format';
import { useSensorValues } from '../live/LiveProvider';
import { LiveValue } from '../live/LiveValue';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import styles from './SensorsTab.module.css';

const errorText = (e: unknown): string => (e instanceof ApiError ? e.message : 'Could not reach the service.');

/** One alias cell: shows the alias; Enter saves, Escape cancels, empty clears. Errors stay inline, the edit stays open. */
const AliasCell: React.FC<{ sensor: Sensor; onSaved: (id: string, alias: string | null) => void }> = ({ sensor, onSaved }) => {
  const [draft, setDraft] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const editButton = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);

  // After Save or Escape the input is gone: give focus back to the edit button.
  useEffect(() => {
    if (draft === null && restoreFocus.current) {
      restoreFocus.current = false;
      editButton.current?.focus();
    }
  }, [draft]);

  const cancel = () => {
    restoreFocus.current = true;
    setDraft(null);
    setError(null);
  };

  const save = async () => {
    if (draft === null || saving) return;
    const alias = draft.trim() || null;
    setSaving(true);
    setError(null);
    try {
      await api<void>('PUT', '/api/sensors/alias', { id: sensor.id, alias });
      onSaved(sensor.id, alias);
      restoreFocus.current = true;
      setDraft(null);
    } catch (e) {
      setError(errorText(e));
    } finally {
      setSaving(false);
    }
  };

  if (draft === null) {
    return (
      <button
        ref={editButton}
        type="button"
        className={styles.editBtn}
        aria-label={`${sensor.alias ?? 'Add alias'} — edit alias of ${sensor.name}`}
        onClick={() => setDraft(sensor.alias ?? '')}
      >
        {sensor.alias ?? <span className={styles.placeholder}>Add alias</span>}
      </button>
    );
  }

  return (
    <div>
      <div className={styles.editRow}>
        <input
          className={styles.input}
          aria-label="Alias"
          autoFocus
          value={draft}
          disabled={saving}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              void save();
            } else if (e.key === 'Escape') {
              e.preventDefault();
              cancel();
            }
          }}
        />
        <Button type="button" disabled={saving} onClick={() => void save()}>
          Save
        </Button>
        <Button type="button" variant="secondary" disabled={saving} onClick={cancel}>
          Cancel
        </Button>
      </div>
      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
    </div>
  );
};

/** Sensor diagnostics as status banners (codes may repeat, so the position is part of the key). */
export const DiagnosticBanners: React.FC<{ diagnostics: SensorsResponse['diagnostics'] }> = ({ diagnostics }) =>
  diagnostics.length === 0 ? null : (
    <div className={styles.banners}>
      {diagnostics.map((d, i) => (
        <Banner key={`${i}:${d.code}`} role="status" data-testid={`diagnostic-${d.code}`}>
          <span>
            {d.message}
            <span className={styles.bannerNote}>See the installation guide.</span>
          </span>
        </Banner>
      ))}
    </div>
  );

export const SensorsTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  const [data, setData] = useState<SensorsResponse | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [roleErrors, setRoleErrors] = useState<Record<string, string>>({});
  const values = useSensorValues();

  const load = useCallback(async () => {
    try {
      setData(await api<SensorsResponse>('GET', '/api/sensors'));
      setLoadError(null);
    } catch (e) {
      setLoadError(errorText(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const treeGroups = useTreeGroups(data, query);
  const groups = useMemo(() => treeGroups.filter((g) => g.label !== 'Roles'), [treeGroups]);
  const byId = useMemo(() => new Map((data?.sensors ?? []).map((s) => [s.id, s])), [data]);

  const aliasSaved = useCallback((id: string, alias: string | null) => {
    setData((d) => (d ? { ...d, sensors: d.sensors.map((s) => (s.id === id ? { ...s, alias } : s)) } : d));
  }, []);

  const pinRole = async (role: string, sensorId: string) => {
    setRoleErrors((r) => ({ ...r, [role]: '' }));
    try {
      await api<void>('PUT', `/api/roles/${encodeURIComponent(role)}`, { sensorId: sensorId || null });
      await load();
    } catch (e) {
      setRoleErrors((r) => ({ ...r, [role]: errorText(e) }));
    }
  };

  if (!data) {
    return (
      <Card title="Sensors">
        {loadError ? (
          <p role="alert" className={styles.error}>
            {loadError}
          </p>
        ) : (
          <p className={styles.muted}>Loading sensors…</p>
        )}
      </Card>
    );
  }

  return (
    <div>
      <DiagnosticBanners diagnostics={data.diagnostics} />

      <Card title="Roles">
        <p className={styles.muted} style={{ marginBottom: 'var(--space-3)' }}>
          A role is a named reading layouts can use. Automatic picks the best sensor; pin one to override.
        </p>
        <div className={styles.roles}>
          {Object.entries(data.roles).map(([role, id]) => {
            const target = id ? byId.get(id) : undefined;
            return (
              <div key={role}>
                <Field label={roleLabel(role)} error={roleErrors[role] || undefined}>
                  <Select value={data.pins[role] ?? ''} onChange={(e) => void pinRole(role, e.target.value)}>
                    <option value="">Automatic</option>
                    {data.sensors.map((s) => (
                      <option key={s.id} value={s.id}>
                        {`${displayName(s)} — ${s.hardware}`}
                      </option>
                    ))}
                  </Select>
                </Field>
                <p className={styles.resolved} data-testid={`role-resolved-${role}`}>
                  Now: {target ? displayName(target) : 'not available'}
                </p>
              </div>
            );
          })}
        </div>
      </Card>

      <Card title="Sensors">
        <div className={styles.toolbar}>
          <input
            type="search"
            className={styles.search}
            aria-label="Search sensors"
            placeholder="Search sensors"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </div>
        {loadError && (
          <p role="alert" className={styles.error}>
            {loadError}
          </p>
        )}
        {groups.length === 0 && (
          <p className={styles.muted}>{data.sensors.length === 0 ? 'No sensors found.' : 'No sensor matches.'}</p>
        )}
        {groups.map((g) => (
          <section key={g.label} className={styles.group}>
            <h3 className={styles.groupTitle}>{g.label}</h3>
            <div className={styles.scroll}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Sensor</th>
                    <th scope="col">Value</th>
                    <th scope="col">Alias</th>
                  </tr>
                </thead>
                <tbody>
                  {g.leaves.map((leaf) => {
                    const s = byId.get(leaf.ref);
                    if (!s) return null;
                    const v = s.id in values ? values[s.id] : s.value;
                    return (
                      <tr key={s.id} data-testid={`sensor-row-${s.id}`}>
                        <td className={styles.name}>
                          {s.name}
                          <span className={styles.sub}>{s.hardware}</span>
                        </td>
                        <td className={styles.value}>
                          <LiveValue>{formatValue(v, s.unit ?? undefined)}</LiveValue>
                        </td>
                        <td className={styles.alias}>
                          <AliasCell sensor={s} onSaved={aliasSaved} />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>
        ))}
      </Card>
    </div>
  );
};

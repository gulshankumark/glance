import React, { useRef, useState } from 'react';
import type { FontSpec, Item, Settings, Threshold } from '../api/types';
import { useTemperatureUnit } from '../live/TemperatureUnit';
import { Button } from '../ui/Button';
import { Field as FieldBox } from '../ui/Field';
import { Select } from '../ui/Select';
import { AssetThumb, IMAGE_KINDS } from './assets';
import { describeItem, matches, sensorOptions } from './catalog';
import { addThreshold, fromShownTemperature, isTemperatureRef, toShownTemperature } from './fields';
import { ColorInput, NumberInput, type Aria } from './inputs';
import { AssetPicker } from './pickers';
import { fieldsFor, LIMITS, type Field } from './schema';
import type { EditorData } from './useEditorData';
import styles from './Editor.module.css';

/** A searchable select of every role and sensor; the current binding is always listed. */
const SensorSelect: React.FC<Aria & { value: string; data: EditorData; onCommit: (v: string) => void }> = ({
  value, data, onCommit, ...aria
}) => {
  const [query, setQuery] = useState('');
  const all = sensorOptions(data.catalog);
  const shown = all.filter((o) => o.value === value || matches(o.label, query));
  if (!all.some((o) => o.value === value)) shown.unshift({ value, label: `Unknown: ${value}` });
  return (
    <span className={styles.stack}>
      <input
        type="search"
        className={styles.input}
        aria-label="Filter sensor list"
        placeholder="Filter"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />
      <Select {...aria} value={value} onChange={(e) => onCommit(e.target.value)}>
        {shown.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </Select>
    </span>
  );
};

const UNIT_LABEL: Record<Settings['unit'], string> = { celsius: '°C', fahrenheit: '°F' };

const WEIGHTS = [100, 200, 300, 400, 500, 600, 700, 800, 900];
const fontSizeField: Field = { key: 'size', label: 'Font size', kind: 'number', ...LIMITS.fontSize, step: 1 };
const thresholdFromField: Field = { key: 'from', label: 'From', kind: 'number', step: 1 };

const FontEditor: React.FC<{ value: FontSpec; fonts: string[] | null; onCommit: (v: FontSpec) => void }> = ({
  value, fonts, onCommit,
}) => {
  const families = fonts ?? [];
  const list = families.includes(value.family) ? families : [value.family, ...families];
  return (
    <>
      <FieldBox label="Font">
        <Select value={value.family} onChange={(e) => onCommit({ ...value, family: e.target.value })}>
          {list.map((f) => (
            <option key={f} value={f}>
              {f}
            </option>
          ))}
        </Select>
      </FieldBox>
      <div className={styles.pair}>
        <FieldBox label="Font size">
          <NumberInput field={fontSizeField} value={value.size} onCommit={(v) => v !== null && onCommit({ ...value, size: v })} />
        </FieldBox>
        <FieldBox label="Font weight">
          <Select value={String(value.weight)} onChange={(e) => onCommit({ ...value, weight: Number(e.target.value) })}>
            {(WEIGHTS.includes(value.weight) ? WEIGHTS : [value.weight, ...WEIGHTS]).map((w) => (
              <option key={w} value={w}>
                {w}
              </option>
            ))}
          </Select>
        </FieldBox>
      </div>
    </>
  );
};

/**
 * Threshold rows keyed by a stable id that follows each row when the list is re-sorted (kept ascending by `from`),
 * so an input that is still focused after Enter keeps editing the same row.
 */
/** `unit` is set when the gauge shows a temperature: `from` is then shown and typed in it, and stored in °C. */
const ThresholdsEditor: React.FC<{ value: Threshold[]; unit: Settings['unit'] | null; onCommit: (v: Threshold[]) => void }> = ({
  value, unit, onCommit,
}) => {
  const nextKey = useRef(0);
  const keys = useRef<number[]>([]);
  if (keys.current.length !== value.length) keys.current = value.map(() => nextKey.current++);
  const commit = (rows: Array<{ t: Threshold; key: number }>) => {
    const sorted = [...rows].sort((a, b) => a.t.from - b.t.from);
    keys.current = sorted.map((r) => r.key);
    onCommit(sorted.map((r) => r.t));
  };
  const rows = () => value.map((t, i) => ({ t, key: keys.current[i] }));
  const set = (i: number, patch: Partial<Threshold>) =>
    commit(rows().map((r, j) => (j === i ? { ...r, t: { ...r.t, ...patch } } : r)));
  return (
    <fieldset className={styles.fieldset}>
      <legend>{unit ? `Thresholds (${UNIT_LABEL[unit]})` : 'Thresholds'}</legend>
      {value.length === 0 && <p className={styles.hint}>None: the gauge uses its colour throughout.</p>}
      {value.map((t, i) => (
        <div key={keys.current[i]} className={styles.thresholdRow}>
          <NumberInput
            aria-label={`Threshold ${i + 1} from`}
            field={thresholdFromField}
            value={unit ? toShownTemperature(t.from, unit) : t.from}
            onCommit={(v) => v !== null && set(i, { from: unit ? fromShownTemperature(v, unit) : v })}
          />
          <ColorInput
            aria-label={`Threshold ${i + 1} colour`}
            pickerLabel={`Threshold ${i + 1} colour picker`}
            value={t.color}
            onCommit={(color) => set(i, { color })}
          />
          <Button
            type="button"
            variant="secondary"
            aria-label={`Remove threshold ${i + 1}`}
            onClick={() => commit(rows().filter((_, j) => j !== i))}
          >
            ✕
          </Button>
        </div>
      ))}
      <Button
        type="button"
        variant="secondary"
        onClick={() => {
          const added = addThreshold(value);
          commit([...rows(), { t: added[added.length - 1], key: nextKey.current++ }]);
        }}
      >
        + Add threshold
      </Button>
    </fieldset>
  );
};

const AssetField: React.FC<{ value: string; data: EditorData; onCommit: (v: string) => void; imageImport?: React.ReactNode }> = ({
  value, data, onCommit, imageImport,
}) => {
  const [picking, setPicking] = useState(false);
  return (
    <div className={styles.assetField}>
      <span className={styles.panelTitle}>Image</span>
      <AssetThumb id={value} />
      <Button type="button" variant="secondary" onClick={() => setPicking(true)}>
        Change image…
      </Button>
      {picking && (
        <AssetPicker
          title="Choose an image"
          assets={data.assets}
          kinds={IMAGE_KINDS}
          onClose={() => setPicking(false)}
          onPick={(a) => {
            setPicking(false);
            onCommit(a.id);
          }}
        >
          {imageImport}
        </AssetPicker>
      )}
    </div>
  );
};

/** Text fields that may be left empty (sent as null: no label / the default clock format). */
const NULLABLE_TEXT = new Set(['label', 'format']);

export interface PropertiesProps {
  item: Item;
  data: EditorData;
  onChange: (patch: Partial<Item>) => void;
  /** Rendered inside the image picker (the import button). */
  imageImport?: React.ReactNode;
}

/** The selected item's properties, generated from schema.ts; each commit is one patch for updateItem. */
export const Properties: React.FC<PropertiesProps> = ({ item, data, onChange, imageImport }) => {
  const rec = item as unknown as Record<string, unknown>;
  const set = (key: string, v: unknown) => onChange({ [key]: v } as Partial<Item>);
  // A gauge bound to a temperature: its minimum, maximum and thresholds are absolute temperatures (stored in °C).
  const chosenUnit = useTemperatureUnit();
  const unit = typeof rec.sensor === 'string' && isTemperatureRef(rec.sensor, data.catalog) ? chosenUnit : null;
  const isLimit = (f: Field) => unit !== null && f.kind === 'number' && (f.key === 'min' || f.key === 'max');

  const control = (f: Field): React.ReactNode => {
    const v = rec[f.key];
    switch (f.kind) {
      case 'text':
        return (
          <FieldBox key={f.key} label={f.label}>
            <input
              className={styles.input}
              value={typeof v === 'string' ? v : ''}
              placeholder={f.key === 'format' ? 'default' : undefined}
              onChange={(e) => set(f.key, NULLABLE_TEXT.has(f.key) && e.target.value === '' ? null : e.target.value)}
            />
          </FieldBox>
        );
      case 'number':
        if (isLimit(f) && unit) {
          const stored = v as number | null | undefined;
          return (
            <FieldBox key={f.key} label={`${f.label} (${UNIT_LABEL[unit]})`}>
              <NumberInput
                field={f}
                value={stored === null || stored === undefined ? stored : toShownTemperature(stored, unit)}
                onCommit={(n) => set(f.key, n === null ? null : fromShownTemperature(n, unit))}
              />
            </FieldBox>
          );
        }
        return (
          <FieldBox key={f.key} label={f.label}>
            <NumberInput field={f} value={v as number | null | undefined} onCommit={(n) => set(f.key, n)} />
          </FieldBox>
        );
      case 'color':
        return (
          <FieldBox key={f.key} label={f.label}>
            <ColorInput value={String(v)} pickerLabel={`${f.label} picker`} onCommit={(c) => set(f.key, c)} />
          </FieldBox>
        );
      case 'select':
        return (
          <FieldBox key={f.key} label={f.label}>
            <Select value={String(v)} options={f.options} onChange={(e) => set(f.key, e.target.value)} />
          </FieldBox>
        );
      case 'bool':
        return (
          <label key={f.key} className={styles.check}>
            <input type="checkbox" checked={v === true} onChange={(e) => set(f.key, e.target.checked)} /> {f.label}
          </label>
        );
      case 'sensor':
        return (
          <FieldBox key={f.key} label={f.label}>
            <SensorSelect value={String(v)} data={data} onCommit={(s) => set(f.key, s)} />
          </FieldBox>
        );
      case 'timezone': {
        const zones = data.timezones ?? [{ id: String(v), label: String(v) }];
        const list = zones.some((z) => z.id === v) ? zones : [{ id: String(v), label: String(v) }, ...zones];
        return (
          <FieldBox key={f.key} label={f.label}>
            <Select value={String(v)} onChange={(e) => set(f.key, e.target.value)}>
              {list.map((z) => (
                <option key={z.id} value={z.id}>
                  {z.label}
                </option>
              ))}
            </Select>
          </FieldBox>
        );
      }
      case 'font':
        return <FontEditor key={f.key} value={v as FontSpec} fonts={data.fonts} onCommit={(font) => set(f.key, font)} />;
      case 'thresholds':
        return <ThresholdsEditor key={f.key} value={(v as Threshold[]) ?? []} unit={unit} onCommit={(t) => set(f.key, t)} />;
      case 'asset':
        return (
          <AssetField key={f.key} value={String(v)} data={data} imageImport={imageImport} onCommit={(a) => set(f.key, a)} />
        );
    }
  };

  return (
    <div data-testid="properties">
      <p className={styles.selectedName}>{describeItem(item, data.catalog)}</p>
      {fieldsFor(item.type).map(control)}
    </div>
  );
};

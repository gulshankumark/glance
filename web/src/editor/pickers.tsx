import React, { useState } from 'react';
import type { Asset, SensorsResponse } from '../api/types';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { Field } from '../ui/Field';
import { AssetGrid } from './assets';
import { matches, sensorOptions } from './catalog';
import styles from './Editor.module.css';

export interface SensorPickerProps {
  title: string;
  catalog: SensorsResponse | null;
  onPick: (ref: string) => void;
  onClose: () => void;
}

/** A dialog listing every role and sensor, with a search box; picking one closes it. */
export const SensorPicker: React.FC<SensorPickerProps> = ({ title, catalog, onPick, onClose }) => {
  const [query, setQuery] = useState('');
  const options = sensorOptions(catalog).filter((o) => matches(o.label, query));
  return (
    <Dialog open title={title} onClose={onClose}>
      <Field label="Search">
        <input
          type="search"
          className={styles.input}
          autoFocus
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </Field>
      {!catalog && <p className={styles.muted}>Loading sensors…</p>}
      {catalog && options.length === 0 && <p className={styles.muted}>No sensor matches.</p>}
      <ul className={styles.pickList}>
        {options.map((o) => (
          <li key={o.value}>
            <button type="button" className={styles.leaf} onClick={() => onPick(o.value)}>
              {o.label}
            </button>
          </li>
        ))}
      </ul>
      <div className={styles.dialogActions}>
        <Button type="button" variant="secondary" onClick={onClose}>
          Cancel
        </Button>
      </div>
    </Dialog>
  );
};

export interface AssetPickerProps {
  title: string;
  assets: Asset[] | null;
  kinds: ReadonlyArray<Asset['kind']>;
  onPick: (asset: Asset) => void;
  onClose: () => void;
  /** Shown under the grid (e.g. an import button). */
  children?: React.ReactNode;
}

/** A dialog with the thumbnails of the assets of the given kinds; picking one closes it. */
export const AssetPicker: React.FC<AssetPickerProps> = ({ title, assets, kinds, onPick, onClose, children }) => (
  <Dialog open title={title} onClose={onClose}>
    <AssetGrid assets={assets} kinds={kinds} onPick={onPick} />
    {children}
    <div className={styles.dialogActions}>
      <Button type="button" variant="secondary" onClick={onClose}>
        Cancel
      </Button>
    </div>
  </Dialog>
);

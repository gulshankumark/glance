import React, { useEffect, useId, useRef, useState } from 'react';
import type { Background } from '../api/types';
import { Field } from '../ui/Field';
import { AssetGrid, ANIMATION_KINDS, IMAGE_KINDS } from './assets';
import { AssetImport } from './AssetImport';
import { ColorInput } from './inputs';
import type { EditorData } from './useEditorData';
import styles from './Editor.module.css';

const MODES: Array<{ type: Background['type']; label: string }> = [
  { type: 'color', label: 'Colour' },
  { type: 'image', label: 'Image' },
  { type: 'animation', label: 'Animation' },
];

export interface BackgroundPickerProps {
  background: Background;
  data: EditorData;
  onChange: (background: Background) => void;
}

/**
 * Colour | Image | Animation. Choosing Image or Animation shows the matching assets and "Import…"; the background
 * changes only once an asset is picked (it never refers to no asset). Going back to Colour restores the last colour.
 */
export const BackgroundPicker: React.FC<BackgroundPickerProps> = ({ background, data, onChange }) => {
  const [mode, setMode] = useState<Background['type']>(background.type);
  const lastColor = useRef(background.type === 'color' ? background.color : '#000000');
  const name = useId();

  useEffect(() => {
    setMode(background.type);
    if (background.type === 'color') lastColor.current = background.color;
  }, [background]);

  const kinds = mode === 'image' ? IMAGE_KINDS : ANIMATION_KINDS;
  const selectedAsset = background.type !== 'color' && background.type === mode ? background.assetId : null;

  return (
    <div data-testid="background-picker">
      <div role="radiogroup" aria-label="Background type" className={styles.segmented}>
        {MODES.map((m) => (
          <label key={m.type} className={styles.segment}>
            <input
              type="radio"
              name={name}
              checked={mode === m.type}
              onChange={() => {
                setMode(m.type);
                if (m.type === 'color') onChange({ type: 'color', color: lastColor.current });
              }}
            />
            {m.label}
          </label>
        ))}
      </div>
      {mode === 'color' && background.type === 'color' && (
        <Field label="Background colour">
          <ColorInput
            value={background.color}
            pickerLabel="Background colour picker"
            onCommit={(color) => onChange({ type: 'color', color })}
          />
        </Field>
      )}
      {mode !== 'color' && (
        <>
          <p className={styles.hint}>
            {mode === 'image' ? 'A still image (PNG or JPEG) behind the items.' : 'A GIF or .zt animation behind the items.'}
          </p>
          <AssetGrid
            assets={data.assets}
            kinds={kinds}
            selectedId={selectedAsset}
            onPick={(a) => onChange({ type: mode, assetId: a.id })}
          />
          <AssetImport
            kinds={kinds}
            reloadAssets={data.reloadAssets}
            onImported={(a) => {
              if (kinds.includes(a.kind)) onChange({ type: mode, assetId: a.id });
            }}
          />
        </>
      )}
    </div>
  );
};

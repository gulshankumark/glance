import React, { useRef, useState } from 'react';
import { upload } from '../api/client';
import type { Asset } from '../api/types';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import { importErrorText, kindFromFileName } from './assets';
import styles from './Editor.module.css';

const EXTENSIONS: Record<Asset['kind'], string[]> = {
  image: ['.png', '.jpg', '.jpeg'],
  gif: ['.gif'],
  zt: ['.zt'],
};
const ROTATIONS = [0, 90, 180, 270];

export interface AssetImportProps {
  /** Kinds this import accepts (the file picker offers their extensions only). */
  kinds: ReadonlyArray<Asset['kind']>;
  /** Called after the upload, once the asset list has been reloaded. */
  onImported: (asset: Asset) => void | Promise<void>;
  reloadAssets: () => Promise<void>;
}

/**
 * "Import…": uploads a file as an asset (POST /api/assets, multipart). The kind comes from the extension
 * (.png/.jpg/.jpeg → image, .gif → gif, .zt → zt); a .zt file first asks for the rotation it was made for.
 */
export const AssetImport: React.FC<AssetImportProps> = ({ kinds, onImported, reloadAssets }) => {
  const input = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<{ name: string; fraction: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [zt, setZt] = useState<{ file: File; rotation: number } | null>(null);
  const accept = kinds.flatMap((k) => EXTENSIONS[k]);

  const send = async (file: File, kind: Asset['kind'], rotation?: number) => {
    const form = new FormData();
    form.append('file', file);
    form.append('kind', kind);
    if (rotation !== undefined) form.append('ztRotation', String(rotation));
    setError(null);
    setProgress({ name: file.name, fraction: 0 });
    try {
      const asset = await upload<Asset>('/api/assets', form, {
        onProgress: (fraction) => setProgress({ name: file.name, fraction }),
      });
      await reloadAssets();
      await onImported(asset);
    } catch (e) {
      setError(importErrorText(e));
    } finally {
      setProgress(null);
    }
  };

  const onFile = (file: File | undefined) => {
    if (!file) return;
    const kind = kindFromFileName(file.name);
    if (!kind || !kinds.includes(kind)) {
      setError(`Choose a ${accept.join(', ')} file.`);
      return;
    }
    if (kind === 'zt') setZt({ file, rotation: 0 });
    else void send(file, kind);
  };

  return (
    <div className={styles.importRow}>
      <input
        ref={input}
        type="file"
        hidden
        aria-label="File to import"
        accept={accept.join(',')}
        onChange={(e) => {
          onFile(e.target.files?.[0]);
          e.target.value = ''; // the same file can be chosen again
        }}
      />
      <Button type="button" variant="secondary" disabled={progress !== null} onClick={() => input.current?.click()}>
        Import…
      </Button>
      {progress && (
        <span className={styles.progress} role="status">
          <progress max={1} value={progress.fraction} aria-label="Upload progress" />
          <span>
            Uploading {progress.name}… {Math.round(progress.fraction * 100)}%
          </span>
        </span>
      )}
      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
      {zt && (
        <Dialog open title={`Import ${zt.file.name}`} onClose={() => setZt(null)}>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              const { file, rotation } = zt;
              setZt(null);
              void send(file, 'zt', rotation);
            }}
          >
            <Field
              label="Rotation"
              hint="Choose the rotation the file was made for (0° unless it was made for a turned screen)."
            >
              <Select value={String(zt.rotation)} onChange={(e) => setZt({ ...zt, rotation: Number(e.target.value) })}>
                {ROTATIONS.map((r) => (
                  <option key={r} value={r}>
                    {r}°
                  </option>
                ))}
              </Select>
            </Field>
            <div className={styles.dialogActions}>
              <Button type="button" variant="secondary" onClick={() => setZt(null)}>
                Cancel
              </Button>
              <Button type="submit">Import</Button>
            </div>
          </form>
        </Dialog>
      )}
    </div>
  );
};

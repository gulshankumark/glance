import React, { useEffect, useState } from 'react';
import { ApiError, getBlob } from '../api/client';
import type { Asset } from '../api/types';
import styles from './Editor.module.css';

/** Assets an image item or image background can show. */
export const IMAGE_KINDS: ReadonlyArray<Asset['kind']> = ['image'];
/** Assets an animation background can play. */
export const ANIMATION_KINDS: ReadonlyArray<Asset['kind']> = ['gif', 'zt'];

/** The asset kind a file is imported as, from its extension; null for files that cannot be imported. */
export function kindFromFileName(name: string): Asset['kind'] | null {
  const ext = /\.([^.]+)$/.exec(name)?.[1].toLowerCase();
  if (ext === 'png' || ext === 'jpg' || ext === 'jpeg') return 'image';
  if (ext === 'gif') return 'gif';
  if (ext === 'zt') return 'zt';
  return null;
}

/** What to tell the user when POST /api/assets failed. */
export function importErrorText(e: unknown): string {
  if (e instanceof ApiError) return e.status === 413 ? 'File is too large (max 64 MB).' : e.errors.join('; ');
  if (e instanceof DOMException && e.name === 'TimeoutError') return 'The upload timed out.';
  if (e instanceof DOMException && e.name === 'AbortError') return 'The upload was cancelled.';
  return 'The file could not be uploaded.';
}

/** "Image 2, 320×240" / "Animation 1, 320×240, 12 frames" (n counts within the shown list, from 1). */
export function assetLabel(asset: Asset, n: number): string {
  const size = `${asset.width}×${asset.height}`;
  return asset.kind === 'image' ? `Image ${n}, ${size}` : `Animation ${n}, ${size}, ${asset.frameCount} frames`;
}

/** The asset's thumbnail (GET /api/assets/{id}/thumbnail.jpg through the api client). */
export const AssetThumb: React.FC<{ id: string }> = ({ id }) => {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let current: string | null = null;
    let cancelled = false;
    getBlob(`/api/assets/${encodeURIComponent(id)}/thumbnail.jpg`)
      .then((blob) => {
        if (cancelled) return;
        current = URL.createObjectURL(blob);
        setUrl(current);
      })
      .catch(() => {
        // no thumbnail: the tile shows its label only
      });
    return () => {
      cancelled = true;
      if (current) URL.revokeObjectURL(current);
    };
  }, [id]);
  return url ? <img src={url} alt="" className={styles.thumb} /> : <span className={styles.thumb} />;
};

export interface AssetGridProps {
  assets: Asset[] | null;
  kinds: ReadonlyArray<Asset['kind']>;
  selectedId?: string | null;
  onPick: (asset: Asset) => void;
}

/** Thumbnails of the assets of the given kinds, as buttons. */
export const AssetGrid: React.FC<AssetGridProps> = ({ assets, kinds, selectedId, onPick }) => {
  if (!assets) return <p className={styles.muted}>Loading files…</p>;
  const shown = assets.filter((a) => kinds.includes(a.kind));
  if (shown.length === 0) return <p className={styles.muted}>No files yet — import one.</p>;
  return (
    <ul className={styles.assetGrid}>
      {shown.map((a, i) => {
        const label = assetLabel(a, i + 1);
        return (
          <li key={a.id}>
            <button
              type="button"
              className={styles.assetTile}
              aria-label={label}
              aria-pressed={selectedId === undefined ? undefined : a.id === selectedId}
              title={label}
              onClick={() => onPick(a)}
            >
              <AssetThumb id={a.id} />
              <span className={styles.assetCaption}>{label}</span>
            </button>
          </li>
        );
      })}
    </ul>
  );
};

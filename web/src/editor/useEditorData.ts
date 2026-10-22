import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Asset, SensorsResponse } from '../api/types';

export interface EditorData {
  /** GET /api/sensors; null until loaded. */
  catalog: SensorsResponse | null;
  /** GET /api/assets; null until loaded. */
  assets: Asset[] | null;
  reloadAssets: () => Promise<void>;
}

/** The lookups the layout editor needs, fetched once when it mounts (failures leave them null; 401 → login). */
export function useEditorData(): EditorData {
  const [catalog, setCatalog] = useState<SensorsResponse | null>(null);
  const [assets, setAssets] = useState<Asset[] | null>(null);

  const reloadAssets = useCallback(async () => {
    try {
      setAssets(await api<Asset[]>('GET', '/api/assets'));
    } catch {
      // keep what we have
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    const load = <T,>(path: string, set: (v: T) => void) =>
      api<T>('GET', path)
        .then((v) => !cancelled && set(v))
        .catch(() => {
          // stays null ("Loading…"); a 401 is handled by the client
        });
    void load<SensorsResponse>('/api/sensors', setCatalog);
    void load<Asset[]>('/api/assets', setAssets);
    return () => {
      cancelled = true;
    };
  }, []);

  return { catalog, assets, reloadAssets };
}

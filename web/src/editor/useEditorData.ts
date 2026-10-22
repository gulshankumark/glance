import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Asset, FontList, SensorsResponse, TimeZone } from '../api/types';

export interface EditorData {
  /** GET /api/sensors; null until loaded. */
  catalog: SensorsResponse | null;
  /** GET /api/assets; null until loaded. */
  assets: Asset[] | null;
  reloadAssets: () => Promise<void>;
  /** GET /api/fonts families (Inter first); null until loaded. */
  fonts: string[] | null;
  /** GET /api/timezones; null until loaded. */
  timezones: TimeZone[] | null;
}

/** The lookups the layout editor needs, fetched once when it mounts (failures leave them null; 401 → login). */
export function useEditorData(): EditorData {
  const [catalog, setCatalog] = useState<SensorsResponse | null>(null);
  const [assets, setAssets] = useState<Asset[] | null>(null);
  const [fonts, setFonts] = useState<string[] | null>(null);
  const [timezones, setTimezones] = useState<TimeZone[] | null>(null);

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
    void load<FontList>('/api/fonts', (f) => setFonts(f.families));
    void load<TimeZone[]>('/api/timezones', setTimezones);
    return () => {
      cancelled = true;
    };
  }, []);

  return { catalog, assets, reloadAssets, fonts, timezones };
}

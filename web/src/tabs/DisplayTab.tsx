import React, { useEffect, useId, useRef, useState } from 'react';
import { api, ApiError, getBlob } from '../api/client';
import type { DeviceView, LayoutSummary, Rotation } from '../api/types';
import { formatValue } from '../lib/format';
import { useDevices } from '../live/LiveProvider';
import { LiveValue } from '../live/LiveValue';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import styles from './DisplayTab.module.css';

const PREVIEW_INTERVAL_MS = 1000;
const ROTATIONS: Rotation[] = [0, 90, 180, 270];

const previewPath = (key: string, t: number) => `/api/preview/${encodeURIComponent(key)}.jpg?t=${t}`;

/**
 * Object URLs of the latest preview frame per device key. Refreshes at most once per PREVIEW_INTERVAL_MS (paced by
 * timestamp, so device pushes never add requests), only while the page is visible, one request per device at a
 * time; a failed request (404 before the first frame) keeps the last image.
 */
function usePreviews(devices: DeviceView[] | null): Record<string, string> {
  const [urls, setUrls] = useState<Record<string, string>>({});
  const devicesRef = useRef(devices);
  devicesRef.current = devices;

  useEffect(() => {
    const current: Record<string, string> = {};
    const inFlight = new Set<string>();
    let lastRefresh = -Infinity;
    let disposed = false;

    const refresh = () => {
      const list = devicesRef.current;
      if (document.visibilityState !== 'visible' || !list?.length) return;
      const now = Date.now();
      if (now - lastRefresh < PREVIEW_INTERVAL_MS) return;
      lastRefresh = now;

      for (const { key } of list) {
        if (inFlight.has(key)) continue;
        inFlight.add(key);
        getBlob(previewPath(key, now))
          .then((blob) => {
            if (disposed) return;
            const old = current[key];
            current[key] = URL.createObjectURL(blob);
            setUrls({ ...current });
            if (old) URL.revokeObjectURL(old);
          })
          .catch(() => {
            // no frame yet (404) or a transient failure: keep showing the last image
          })
          .finally(() => inFlight.delete(key));
      }
    };

    refresh();
    const timer = setInterval(refresh, PREVIEW_INTERVAL_MS / 4);
    document.addEventListener('visibilitychange', refresh);
    return () => {
      disposed = true;
      clearInterval(timer);
      document.removeEventListener('visibilitychange', refresh);
      Object.values(current).forEach((u) => URL.revokeObjectURL(u));
    };
  }, []);

  return urls;
}

interface DeviceCardProps {
  device: DeviceView;
  layouts: LayoutSummary[];
  previewUrl: string | undefined;
  navigate: (hash: string) => void;
  onUpdated: (device: DeviceView) => void;
}

const DeviceCard: React.FC<DeviceCardProps> = ({ device, layouts, previewUrl, navigate, onUpdated }) => {
  const rotationLabelId = useId();
  const [error, setError] = useState<string | null>(null);

  const update = async (patch: { rotation?: Rotation; activeLayout?: string | null }) => {
    setError(null);
    try {
      onUpdated(await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(device.key)}`, patch));
    } catch (err) {
      setError(err instanceof ApiError ? err.errors.join(', ') : 'The change could not be saved.');
    }
  };

  return (
    <Card data-testid={`device-card-${device.key}`} title={device.name}>
      <div className={styles.metaRow}>
        <span>
          <strong>
            {device.width}×{device.height}
          </strong>
          <span style={{ color: 'var(--muted)', marginLeft: 'var(--space-2)' }}>• {device.status}</span>
        </span>
        <LiveValue className={styles.fpsBadge}>{formatValue(device.fps, 'fps', 1)}</LiveValue>
      </div>

      {device.status === 'unsupported' && (
        <p className={styles.statusMessage}>{device.message ?? 'This screen is not supported.'}</p>
      )}
      {device.status === 'retrying' && (
        <p className={styles.statusMessage}>
          {device.message ?? 'Connection lost, retrying…'}
          {device.nextRetry && ` (next try ${new Date(device.nextRetry).toLocaleTimeString()})`}
        </p>
      )}

      <div className={styles.previewWrapper}>
        {previewUrl ? (
          <img
            data-testid="device-preview"
            className={styles.previewImage}
            src={previewUrl}
            alt={`What ${device.name} shows now`}
          />
        ) : (
          <span style={{ color: 'var(--muted)' }}>Waiting for the first frame…</span>
        )}
      </div>

      {!device.activeLayout && (
        <div className={styles.ctaBox}>
          <span style={{ fontSize: '0.9rem' }}>No layout on this screen yet</span>
          <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
            <Button variant="secondary" onClick={() => navigate('#layouts')}>
              Choose a layout
            </Button>
            <Button variant="primary" onClick={() => navigate('#layouts/new')}>
              Create a layout
            </Button>
          </div>
        </div>
      )}

      <div className={styles.controlsRow}>
        <Field label="Active layout" error={error ?? undefined}>
          <Select value={device.activeLayout ?? ''} onChange={(e) => update({ activeLayout: e.target.value || null })}>
            <option value="">None (built-in status screen)</option>
            {layouts.map((l) => (
              <option key={l.name} value={l.name} disabled={l.error !== null}>
                {l.error === null ? `${l.name} (${l.width}×${l.height})` : `${l.name} (cannot be loaded)`}
              </option>
            ))}
          </Select>
        </Field>

        <div>
          <div id={rotationLabelId} className={styles.groupLabel}>
            Rotation
          </div>
          <div role="group" aria-labelledby={rotationLabelId} className={styles.rotationGroup}>
            <Button variant="primary" onClick={() => update({ rotation: ((device.rotation + 180) % 360) as Rotation })}>
              Flip 180°
            </Button>
            {ROTATIONS.map((r) => (
              <Button
                key={r}
                variant={device.rotation === r ? 'primary' : 'secondary'}
                aria-pressed={device.rotation === r}
                onClick={() => update({ rotation: r })}
              >
                {r}°
              </Button>
            ))}
          </div>
        </div>
      </div>
    </Card>
  );
};

export const DisplayTab: React.FC<{ navigate: (hash: string) => void }> = ({ navigate }) => {
  const liveDevices = useDevices();
  const [devices, setDevices] = useState<DeviceView[] | null>(null);
  const [layouts, setLayouts] = useState<LayoutSummary[]>([]);
  const previews = usePreviews(devices);

  useEffect(() => {
    api<DeviceView[]>('GET', '/api/devices')
      .then((list) => setDevices((prev) => prev ?? list))
      .catch(() => {
        // a 401 is handled by the client; otherwise the live stream fills the list
      });
    api<LayoutSummary[]>('GET', '/api/layouts')
      .then(setLayouts)
      .catch(() => {});
  }, []);

  // The event stream always carries the full current list.
  useEffect(() => {
    if (liveDevices) setDevices(liveDevices);
  }, [liveDevices]);

  const onUpdated = (updated: DeviceView) =>
    setDevices((list) => list?.map((d) => (d.key === updated.key ? updated : d)) ?? list);

  return (
    <div className={styles.container}>
      <h2>Display</h2>
      {devices === null && <p style={{ color: 'var(--muted)' }}>Looking for screens…</p>}
      {devices?.length === 0 && (
        <Card>
          <p style={{ color: 'var(--muted)', fontSize: '1rem' }}>No screen found — plug in a supported display</p>
        </Card>
      )}
      {devices && devices.length > 0 && (
        <div className={styles.deviceGrid}>
          {devices.map((device) => (
            <DeviceCard
              key={device.key}
              device={device}
              layouts={layouts}
              previewUrl={previews[device.key]}
              navigate={navigate}
              onUpdated={onUpdated}
            />
          ))}
        </div>
      )}
    </div>
  );
};

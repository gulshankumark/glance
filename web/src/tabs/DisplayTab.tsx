import React, { useEffect, useId, useRef, useState } from 'react';
import { getBlob } from '../api/client';
import type { DeviceView, LayoutSummary, Rotation } from '../api/types';
import { useDeviceList } from '../devices/useDeviceList';
import { LayoutSelect, ROTATIONS, useDeviceUpdate } from '../devices/DeviceControls';
import { formatNextRetry, formatValue } from '../lib/format';
import { LiveValue } from '../live/LiveValue';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import styles from './DisplayTab.module.css';

const PREVIEW_INTERVAL_MS = 1000;

const previewPath = (key: string, t: number) => `/api/preview/${encodeURIComponent(key)}.jpg?t=${t}`;

/**
 * Object URLs of the latest preview frame per device key. Refreshes once per PREVIEW_INTERVAL_MS (the next refresh is
 * timed for lastRefresh + PREVIEW_INTERVAL_MS, so device pushes never add requests), only while the page is visible,
 * one request per device at a time; a failed request (404 before the first frame) keeps the last image.
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
    let timer: ReturnType<typeof setTimeout> | undefined;

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

    // Refresh if due, then wake up exactly when the next one is due; nothing is scheduled while hidden. Until the
    // device list has loaded, or while it is empty (nothing refreshed though due), check again every quarter interval.
    const tick = () => {
      clearTimeout(timer);
      if (disposed) return;
      refresh();
      if (document.visibilityState !== 'visible') return;
      const wait = lastRefresh + PREVIEW_INTERVAL_MS - Date.now();
      timer = setTimeout(tick, wait > 0 ? wait : PREVIEW_INTERVAL_MS / 4);
    };

    tick();
    document.addEventListener('visibilitychange', tick);
    return () => {
      disposed = true;
      clearTimeout(timer);
      document.removeEventListener('visibilitychange', tick);
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
  const rotationErrorId = useId();
  const { update, layoutError, rotationError } = useDeviceUpdate(device, onUpdated);

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
          {formatNextRetry(device.nextRetry) && ` (next try ${formatNextRetry(device.nextRetry)})`}
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
        <LayoutSelect device={device} layouts={layouts} onChange={(activeLayout) => update({ activeLayout })} error={layoutError ?? undefined} />

        <div>
          <div id={rotationLabelId} className={styles.groupLabel}>
            Rotation
          </div>
          <div
            role="group"
            aria-labelledby={rotationLabelId}
            aria-describedby={rotationError ? rotationErrorId : undefined}
            className={styles.rotationGroup}
          >
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
          {rotationError && (
            <p id={rotationErrorId} role="alert" style={{ marginTop: 'var(--space-1)', fontSize: '0.8rem', color: 'var(--danger)' }}>
              {rotationError}
            </p>
          )}
        </div>
      </div>
    </Card>
  );
};

export const DisplayTab: React.FC<{ navigate: (hash: string) => void }> = ({ navigate }) => {
  const { devices, layouts, onUpdated } = useDeviceList();
  const previews = usePreviews(devices);

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

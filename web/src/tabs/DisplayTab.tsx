import React, { useEffect, useState, useRef } from 'react';
import { api } from '../api/client';
import type { DeviceView } from '../api/types';
import { useDevices } from '../live/LiveProvider';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import styles from './DisplayTab.module.css';

interface LayoutSummary {
  name: string;
  width: number;
  height: number;
}

export const DisplayTab: React.FC<{ navigate: (hash: string) => void }> = ({ navigate }) => {
  const liveDevices = useDevices();
  const [initialDevices, setInitialDevices] = useState<DeviceView[] | null>(null);
  const [layouts, setLayouts] = useState<LayoutSummary[]>([]);
  const [previewTimestamps, setPreviewTimestamps] = useState<Record<string, number>>({});
  const lastGoodSrcRef = useRef<Record<string, string>>({});

  // Fetch initial devices and layouts on mount
  useEffect(() => {
    api<DeviceView[]>('GET', '/api/devices')
      .then(setInitialDevices)
      .catch(() => {});

    api<LayoutSummary[]>('GET', '/api/layouts')
      .then(setLayouts)
      .catch(() => {});
  }, []);

  const devices = liveDevices ?? initialDevices;

  // Refresh preview images every 1s while document is visible
  useEffect(() => {
    const updatePreviews = () => {
      if (document.visibilityState === 'visible') {
        const now = Date.now();
        setPreviewTimestamps((prev) => {
          const next: Record<string, number> = { ...prev };
          if (devices) {
            for (const dev of devices) {
              next[dev.key] = now;
            }
          }
          return next;
        });
      }
    };

    updatePreviews();
    const interval = setInterval(updatePreviews, 1000);

    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        updatePreviews();
      }
    };
    document.addEventListener('visibilitychange', onVisibilityChange);

    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, [devices]);

  const handleLayoutChange = async (device: DeviceView, newLayout: string) => {
    const activeLayout = newLayout === '' ? null : newLayout;
    try {
      await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(device.key)}`, {
        activeLayout,
      });
      if (initialDevices) {
        setInitialDevices(
          initialDevices.map((d) => (d.key === device.key ? { ...d, activeLayout } : d))
        );
      }
    } catch {
      // Handled by api client
    }
  };

  const handleRotationChange = async (device: DeviceView, rotation: 0 | 90 | 180 | 270) => {
    try {
      await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(device.key)}`, {
        rotation,
      });
      if (initialDevices) {
        setInitialDevices(
          initialDevices.map((d) => (d.key === device.key ? { ...d, rotation } : d))
        );
      }
    } catch {
      // Handled by api client
    }
  };

  if (!devices) {
    return (
      <div className={styles.container}>
        <h2>Display</h2>
        <p style={{ color: 'var(--muted)' }}>Loading display devices…</p>
      </div>
    );
  }

  if (devices.length === 0) {
    return (
      <div className={styles.container}>
        <h2>Display</h2>
        <Card title="No Display Detected">
          <p style={{ color: 'var(--muted)', fontSize: '1rem', padding: 'var(--space-2) 0' }}>
            No screen found — plug in a supported display
          </p>
        </Card>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <h2>Display</h2>
      <div className={styles.deviceGrid}>
        {devices.map((device) => {
          const t = previewTimestamps[device.key] ?? Date.now();
          const previewSrc = `/api/preview/${encodeURIComponent(device.key)}.jpg?t=${t}`;

          const isUnsupported = device.status === 'unsupported';
          const isRetrying = device.status === 'retrying';

          return (
            <Card
              key={device.key}
              data-testid={`device-card-${device.key}`}
              title={device.name}
            >
              {/* Device metadata */}
              <div className={styles.metaRow}>
                <span>
                  <strong>{device.width}x{device.height}</strong>
                  <span style={{ color: 'var(--muted)', marginLeft: 'var(--space-2)' }}>
                    • {device.status}
                  </span>
                </span>
                <span className={styles.fpsBadge}>
                  {(device.fps ?? 0).toFixed(1)} fps
                </span>
              </div>

              {/* Status alerts */}
              {isUnsupported && (
                <p style={{ color: 'var(--warning)', marginBottom: 'var(--space-3)' }}>
                  {device.message || 'Unsupported display device'}
                </p>
              )}
              {isRetrying && (
                <p style={{ color: 'var(--warning)', marginBottom: 'var(--space-3)' }}>
                  {device.message || 'Connection lost, retrying…'}{' '}
                  {device.nextRetry && `(next retry: ${new Date(device.nextRetry).toLocaleTimeString()})`}
                </p>
              )}

              {/* Live Preview Image */}
              <div className={styles.previewWrapper}>
                <img
                  data-testid="device-preview"
                  className={styles.previewImage}
                  src={previewSrc}
                  alt={`Live preview for ${device.name}`}
                  onLoad={(e) => {
                    lastGoodSrcRef.current[device.key] = e.currentTarget.src;
                  }}
                  onError={(e) => {
                    // Keep last successful image on 404
                    if (lastGoodSrcRef.current[device.key]) {
                      e.currentTarget.src = lastGoodSrcRef.current[device.key];
                    }
                  }}
                />
              </div>

              {/* First run call-to-action if no layout is active */}
              {!device.activeLayout && (
                <div className={styles.ctaBox}>
                  <span style={{ fontSize: '0.9rem' }}>No active layout on this display</span>
                  <div style={{ display: 'flex', gap: 'var(--space-2)' }}>
                    <Button variant="secondary" onClick={() => navigate('#layouts')}>
                      Choose a layout
                    </Button>
                    <Button variant="primary" onClick={() => navigate('#layouts')}>
                      Create a layout
                    </Button>
                  </div>
                </div>
              )}

              {/* Controls */}
              <div className={styles.controlsRow}>
                <Field label="Active Layout">
                  <Select
                    aria-label="Active Layout"
                    value={device.activeLayout ?? ''}
                    onChange={(e) => handleLayoutChange(device, e.target.value)}
                  >
                    <option value="">None (built-in screen)</option>
                    {layouts.map((l) => (
                      <option key={l.name} value={l.name}>
                        {l.name} ({l.width}x{l.height})
                      </option>
                    ))}
                  </Select>
                </Field>

                <Field label="Orientation & Rotation">
                  <div className={styles.rotationGroup}>
                    <Button
                      variant="primary"
                      onClick={() =>
                        handleRotationChange(
                          device,
                          ((device.rotation + 180) % 360) as 0 | 90 | 180 | 270
                        )
                      }
                    >
                      Flip 180°
                    </Button>
                    {([0, 90, 180, 270] as const).map((r) => (
                      <Button
                        key={r}
                        variant={device.rotation === r ? 'primary' : 'secondary'}
                        onClick={() => handleRotationChange(device, r)}
                      >
                        {r}°
                      </Button>
                    ))}
                  </div>
                </Field>
              </div>
            </Card>
          );
        })}
      </div>
    </div>
  );
};

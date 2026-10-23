import React from 'react';
import type { DeviceView, LayoutSummary, Rotation } from '../api/types';
import { Calibration } from '../devices/Calibration';
import { LayoutSelect, ROTATIONS, useDeviceUpdate } from '../devices/DeviceControls';
import { useDeviceList } from '../devices/useDeviceList';
import { formatNextRetry } from '../lib/format';
import { Card } from '../ui/Card';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import styles from './DevicesTab.module.css';

interface DeviceEntryProps {
  device: DeviceView;
  layouts: LayoutSummary[];
  onUpdated: (device: DeviceView) => void;
}

const DeviceEntry: React.FC<DeviceEntryProps> = ({ device, layouts, onUpdated }) => {
  const { update, layoutError, rotationError } = useDeviceUpdate(device, onUpdated);
  const retryAt = formatNextRetry(device.nextRetry);

  return (
    <Card data-testid={`device-card-${device.key}`} title={device.name}>
      <dl className={styles.facts}>
        <dt>Driver</dt>
        <dd>{device.driverId}</dd>
        <dt>Model code</dt>
        <dd>{device.modelCode}</dd>
        <dt>Serial</dt>
        <dd>{device.serial || '—'}</dd>
        <dt>Status</dt>
        <dd>{device.status}</dd>
      </dl>

      {device.status === 'retrying' && (
        <p className={styles.warning}>
          {device.message ?? 'Connection lost, retrying…'}
          {retryAt && ` (next try ${retryAt})`}
        </p>
      )}

      {device.status === 'unsupported' ? (
        <>
          <p className={styles.warning}>Connected — unsupported model (code {device.modelCode})</p>
          <p style={{ color: 'var(--muted)' }}>
            Support can be added by contributors; see CONTRIBUTING.md in the project repository.
          </p>
        </>
      ) : (
        <>
          {device.layoutMismatch && (
            <p className={styles.warning}>
              The active layout does not match this screen&apos;s size ({device.width}×{device.height}).
            </p>
          )}
          <LayoutSelect
            device={device}
            layouts={layouts}
            onChange={(activeLayout) => update({ activeLayout })}
            error={layoutError ?? undefined}
          />
          <Field label="Rotation" error={rotationError ?? undefined}>
            <Select
              value={String(device.rotation)}
              onChange={(e) => update({ rotation: Number(e.target.value) as Rotation })}
            >
              {ROTATIONS.map((r) => (
                <option key={r} value={r}>
                  {r}°
                </option>
              ))}
            </Select>
          </Field>
          {device.status === 'connected' && <Calibration device={device} onUpdated={onUpdated} />}
        </>
      )}
    </Card>
  );
};

export const DevicesTab: React.FC<{ navigate: (hash: string) => void }> = () => {
  const { devices, layouts, onUpdated } = useDeviceList();

  return (
    <div className={styles.container}>
      <h2>Devices</h2>
      {devices === null && <p style={{ color: 'var(--muted)' }}>Looking for screens…</p>}
      {devices?.length === 0 && (
        <Card>
          <p style={{ color: 'var(--muted)' }}>No screen found — plug in a supported display</p>
        </Card>
      )}
      {devices && devices.length > 0 && (
        <div className={styles.list}>
          {devices.map((d) => (
            <DeviceEntry key={d.key} device={d} layouts={layouts} onUpdated={onUpdated} />
          ))}
        </div>
      )}
    </div>
  );
};

import React, { useState } from 'react';
import { api, ApiError } from '../api/client';
import type { DeviceView, LayoutSummary, Rotation } from '../api/types';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';

export type DevicePatch = { rotation?: Rotation; activeLayout?: string | null };

/**
 * PUT /api/devices/{key}. The answer is applied through onUpdated at once; a failure is kept in `error`
 * (never silently reverted: the shown state only changes with a successful answer).
 */
export function useDeviceUpdate(device: DeviceView, onUpdated: (device: DeviceView) => void) {
  const [error, setError] = useState<string | null>(null);

  const update = async (patch: DevicePatch) => {
    setError(null);
    try {
      onUpdated(await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(device.key)}`, patch));
    } catch (err) {
      setError(err instanceof ApiError ? err.errors.join(', ') : 'The change could not be saved.');
    }
  };

  return { update, error };
}

interface LayoutSelectProps {
  device: DeviceView;
  layouts: LayoutSummary[];
  onChange: (activeLayout: string | null) => void;
  error?: string;
}

/** The "Active layout" select; "None" clears the layout (activeLayout: null). */
export const LayoutSelect: React.FC<LayoutSelectProps> = ({ device, layouts, onChange, error }) => (
  <Field label="Active layout" error={error}>
    <Select value={device.activeLayout ?? ''} onChange={(e) => onChange(e.target.value || null)}>
      <option value="">None (built-in status screen)</option>
      {layouts.map((l) => (
        <option key={l.name} value={l.name} disabled={l.error !== null}>
          {l.error === null ? `${l.name} (${l.width}×${l.height})` : `${l.name} (cannot be loaded)`}
        </option>
      ))}
    </Select>
  </Field>
);

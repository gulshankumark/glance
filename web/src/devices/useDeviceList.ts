import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { DeviceView, LayoutSummary } from '../api/types';
import { useDevices } from '../live/LiveProvider';

/**
 * The device list (initial GET, then the full list from every event-stream push), the layout summaries, and
 * `onUpdated` to apply a PUT answer at once instead of waiting for the next push.
 */
export function useDeviceList() {
  const liveDevices = useDevices();
  const [devices, setDevices] = useState<DeviceView[] | null>(null);
  const [layouts, setLayouts] = useState<LayoutSummary[]>([]);

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

  return { devices, layouts, onUpdated };
}

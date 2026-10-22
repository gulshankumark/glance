import React, { createContext, useContext, useEffect, useState } from 'react';
import type { DeviceView } from '../api/types';

export type LiveStatus = 'connecting' | 'open' | 'reconnecting';

interface LiveContextType {
  status: LiveStatus;
  sensorValues: Record<string, number | null>;
  devices: DeviceView[] | null;
}

const LiveContext = createContext<LiveContextType>({
  status: 'connecting',
  sensorValues: {},
  devices: null,
});

export const useLiveStatus = () => useContext(LiveContext).status;
export const useSensorValues = () => useContext(LiveContext).sensorValues;
export const useDevices = () => useContext(LiveContext).devices;

export const LiveProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [status, setStatus] = useState<LiveStatus>('connecting');
  const [sensorValues, setSensorValues] = useState<Record<string, number | null>>({});
  const [devices, setDevices] = useState<DeviceView[] | null>(null);

  useEffect(() => {
    // Listen for test simulated status in browser tests
    const testHandler = (e: Event) => {
      const custom = e as CustomEvent<LiveStatus>;
      if (custom.detail) {
        setStatus(custom.detail);
      }
    };
    window.addEventListener('test-live-status', testHandler);

    let es: EventSource | null = null;
    let reconnectTimeout: ReturnType<typeof setTimeout> | null = null;
    let attempt = 0;
    const backoffs = [1000, 2000, 5000];

    const connect = () => {
      if (reconnectTimeout) {
        clearTimeout(reconnectTimeout);
        reconnectTimeout = null;
      }

      setStatus((prev) => (prev === 'connecting' ? 'connecting' : 'reconnecting'));
      es = new EventSource('/api/events');

      es.onopen = () => {
        attempt = 0;
        setStatus('open');
      };

      es.addEventListener('sensors', (e) => {
        setStatus('open');
        try {
          const data = JSON.parse(e.data);
          setSensorValues((prev) => ({ ...prev, ...data }));
        } catch {
          // ignore
        }
      });

      es.addEventListener('devices', (e) => {
        setStatus('open');
        try {
          const data = JSON.parse(e.data);
          setDevices(data);
        } catch {
          // ignore
        }
      });

      es.onerror = () => {
        setStatus('reconnecting');
        if (es) {
          es.close();
          es = null;
        }
        const delay = backoffs[Math.min(attempt, backoffs.length - 1)];
        attempt++;
        reconnectTimeout = setTimeout(connect, delay);
      };
    };

    connect();

    return () => {
      window.removeEventListener('test-live-status', testHandler);
      if (es) es.close();
      if (reconnectTimeout) clearTimeout(reconnectTimeout);
    };
  }, []);

  return (
    <LiveContext.Provider value={{ status, sensorValues, devices }}>
      {children}
    </LiveContext.Provider>
  );
};

import React, { createContext, useContext, useEffect, useMemo, useState } from 'react';
import { api, notifyUnauthorized } from '../api/client';
import type { AuthState, DeviceView } from '../api/types';

/** 'connecting' until the stream has opened once; 'reconnecting' after it dropped. */
export type LiveStatus = 'connecting' | 'open' | 'reconnecting';

export interface LiveContextValue {
  status: LiveStatus;
  /** Live values are stale while not open, and after (re)opening until the next `sensors` event (docs/api.md). */
  stale: boolean;
  sensorValues: Record<string, number | null>;
  devices: DeviceView[] | null;
}

// Three contexts, so a sensors tick (every second) re-renders only the components that read sensor values.
const StatusContext = createContext<{ status: LiveStatus; stale: boolean }>({ status: 'connecting', stale: true });
const SensorValuesContext = createContext<Record<string, number | null>>({});
const DevicesContext = createContext<DeviceView[] | null>(null);

export const useLiveStatus = () => useContext(StatusContext).status;
export const useLiveStale = () => useContext(StatusContext).stale;
export const useSensorValues = () => useContext(SensorValuesContext);
export const useDevices = () => useContext(DevicesContext);

/** Provides live state to the hooks above (LiveProvider uses it; tests can give a fixed state). */
export const LiveStateProvider: React.FC<{ value: LiveContextValue; children: React.ReactNode }> = ({ value, children }) => {
  const { status, stale, sensorValues, devices } = value;
  const statusValue = useMemo(() => ({ status, stale }), [status, stale]);
  return (
    <StatusContext.Provider value={statusValue}>
      <SensorValuesContext.Provider value={sensorValues}>
        <DevicesContext.Provider value={devices}>{children}</DevicesContext.Provider>
      </SensorValuesContext.Provider>
    </StatusContext.Provider>
  );
};

const BACKOFF_MS = [1000, 2000, 5000];

export const LiveProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [status, setStatus] = useState<LiveStatus>('connecting');
  const [fresh, setFresh] = useState(false);
  const [sensorValues, setSensorValues] = useState<Record<string, number | null>>({});
  const [devices, setDevices] = useState<DeviceView[] | null>(null);

  useEffect(() => {
    let es: EventSource | null = null;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;
    let attempt = 0;
    let opened = false;
    let disposed = false;

    const connect = () => {
      retryTimer = null;
      const source = new EventSource('/api/events');
      es = source;

      source.onopen = () => {
        opened = true;
        attempt = 0;
        setStatus('open');
        setFresh(false);
      };

      source.addEventListener('sensors', (e) => {
        try {
          setSensorValues(JSON.parse((e as MessageEvent).data));
          setFresh(true);
        } catch {
          // malformed payload: keep the previous values
        }
      });

      source.addEventListener('devices', (e) => {
        try {
          setDevices(JSON.parse((e as MessageEvent).data));
        } catch {
          // malformed payload: keep the previous list
        }
      });

      source.onerror = () => {
        source.close();
        if (es !== source || disposed) return;
        es = null;
        setStatus(opened ? 'reconnecting' : 'connecting');
        setFresh(false);
        const delay = BACKOFF_MS[Math.min(attempt, BACKOFF_MS.length - 1)];
        attempt++;
        retryTimer = setTimeout(connect, delay);
        // A remote caller whose login expired gets errors forever: send it to the login page instead.
        api<AuthState>('GET', '/api/auth/state')
          .then((state) => {
            if (disposed || state.loopback || state.authenticated) return;
            if (retryTimer) clearTimeout(retryTimer);
            retryTimer = null;
            disposed = true;
            notifyUnauthorized();
          })
          .catch(() => {
            // service unreachable: keep retrying
          });
      };
    };

    connect();

    return () => {
      disposed = true;
      es?.close();
      if (retryTimer) clearTimeout(retryTimer);
    };
  }, []);

  const stale = status !== 'open' || !fresh;

  return (
    <LiveStateProvider value={{ status, stale, sensorValues, devices }}>{children}</LiveStateProvider>
  );
};

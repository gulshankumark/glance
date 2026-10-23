import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { Settings } from '../api/types';
import { formatSensorValue } from '../lib/format';
import { useDevices } from './LiveProvider';

type Unit = Settings['unit'];

const UnitContext = createContext<{ unit: Unit; setUnit: (unit: Unit) => void }>({
  unit: 'celsius',
  setUnit: () => undefined,
});

/**
 * The temperature unit setting for every value shown in the UI. Read from GET /api/settings at start and again after
 * each `devices` event (the service sends one whenever any setting changes); the Settings tab sets it on save.
 * Must sit inside LiveProvider.
 */
export const TemperatureUnitProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [unit, setUnitState] = useState<Unit>('celsius');
  const devices = useDevices();
  const seq = useRef(0);

  const setUnit = useCallback((next: Unit) => {
    seq.current++; // an answer still on its way is older than this
    setUnitState(next);
  }, []);

  useEffect(() => {
    const mine = ++seq.current;
    api<Settings>('GET', '/api/settings').then(
      (s) => {
        if (mine === seq.current) setUnitState(s.unit);
      },
      () => {
        // keep the unit we have; a 401 is handled by the client
      },
    );
  }, [devices]);

  return <UnitContext.Provider value={{ unit, setUnit }}>{children}</UnitContext.Provider>;
};

export const useSetTemperatureUnit = () => useContext(UnitContext).setUnit;

/** Formats a sensor value in the chosen temperature unit (see formatSensorValue). */
export function useFormatSensorValue() {
  const { unit } = useContext(UnitContext);
  return useCallback(
    (value: number | null | undefined, sensorUnit: string | null | undefined, decimals = 0) =>
      formatSensorValue(value, sensorUnit, unit, decimals),
    [unit],
  );
}

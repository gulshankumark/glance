import React, { useEffect, useId, useRef, useState } from 'react';
import { api, ApiError, setTestPattern } from '../api/client';
import type { DeviceView } from '../api/types';
import { Button } from '../ui/Button';
import type { DevicePatch } from './DeviceControls';
import styles from './Calibration.module.css';

type Values = { gamma: number; brightness: number };

const DEFAULTS: Values = { gamma: 1, brightness: 100 };
const DEBOUNCE_MS = 150;
/** The service ends the test pattern by itself after this long. */
const PATTERN_MS = 120_000;
/** The same 16 grey steps (0..255) the test pattern draws on the screen. */
const STEPS = Array.from({ length: 16 }, (_, i) => i * 17);

const errorText = (err: unknown, fallback: string) => (err instanceof ApiError ? err.errors.join(', ') : fallback);

interface SliderProps {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  shown: string;
  onChange: (value: number) => void;
}

const Slider: React.FC<SliderProps> = ({ label, value, min, max, step, shown, onChange }) => {
  const id = useId();
  return (
    <div className={styles.slider}>
      <div className={styles.sliderHead}>
        <label htmlFor={id}>{label}</label>
        <output htmlFor={id}>{shown}</output>
      </div>
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </div>
  );
};

/**
 * "Screen colour": gamma and brightness of the bytes sent to one connected screen, plus a test pattern to judge them.
 * Slider changes PUT after 150 ms without change; only the answer to the latest PUT counts. A failed PUT shows the
 * server error and restores the last saved values.
 */
export const Calibration: React.FC<{ device: DeviceView; onUpdated: (device: DeviceView) => void }> = ({
  device,
  onUpdated,
}) => {
  const [values, setValues] = useState<Values>({ gamma: device.gamma, brightness: device.brightness });
  const [error, setError] = useState<string | null>(null);
  const [patternOn, setPatternOn] = useState(false);

  const key = useRef(device.key);
  const onUpdatedRef = useRef(onUpdated);
  onUpdatedRef.current = onUpdated;
  const saved = useRef<Values>({ gamma: device.gamma, brightness: device.brightness });
  const pending = useRef<Values | null>(null);
  const debounce = useRef<number | undefined>(undefined);
  const sent = useRef(0);
  const inFlight = useRef(0);
  const patternTimer = useRef<number | undefined>(undefined);
  const patternState = useRef({ on: false, busy: false, unmounted: false });

  // Adopt values changed elsewhere (live pushes, other browsers) while none of ours is pending.
  useEffect(() => {
    saved.current = { gamma: device.gamma, brightness: device.brightness };
    if (pending.current === null && inFlight.current === 0) setValues(saved.current);
  }, [device.gamma, device.brightness]);

  const send = async (v: Values) => {
    const seq = ++sent.current;
    inFlight.current++;
    try {
      const patch: DevicePatch = v;
      const updated = await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(key.current)}`, patch);
      if (seq !== sent.current) return;
      saved.current = { gamma: updated.gamma, brightness: updated.brightness };
      setError(null);
      onUpdatedRef.current(updated);
    } catch (err) {
      if (seq !== sent.current) return;
      setError(errorText(err, 'The change could not be saved.'));
      if (pending.current === null) setValues(saved.current);
    } finally {
      inFlight.current--;
    }
  };
  const sendRef = useRef(send);
  sendRef.current = send;

  const change = (v: Values) => {
    setValues(v);
    pending.current = v;
    window.clearTimeout(debounce.current);
    debounce.current = window.setTimeout(() => {
      pending.current = null;
      void sendRef.current(v);
    }, DEBOUNCE_MS);
  };

  const togglePattern = async () => {
    const state = patternState.current;
    if (state.busy) return;
    const on = !state.on;
    state.busy = true;
    try {
      await setTestPattern(key.current, on);
    } catch (err) {
      setError(errorText(err, 'The test pattern could not be changed.'));
      return;
    } finally {
      state.busy = false;
    }
    if (state.unmounted) {
      // Left the page while the POST was under way: end the pattern now.
      if (on) setTestPattern(key.current, false).catch(() => {});
      return;
    }
    setError(null);
    window.clearTimeout(patternTimer.current);
    state.on = on;
    setPatternOn(on);
    if (on) {
      patternTimer.current = window.setTimeout(() => {
        state.on = false;
        setPatternOn(false);
      }, PATTERN_MS);
    }
  };

  useEffect(
    () => () => {
      // Leaving the page: send a change still waiting for its debounce, and end a test pattern that is on.
      window.clearTimeout(debounce.current);
      window.clearTimeout(patternTimer.current);
      if (pending.current !== null) {
        const v = pending.current;
        pending.current = null;
        void sendRef.current(v);
      }
      const state = patternState.current;
      state.unmounted = true;
      if (state.on) setTestPattern(key.current, false).catch(() => {});
    },
    [],
  );

  return (
    <>
      <fieldset className={styles.fieldset}>
        <legend>Screen colour</legend>
        <Slider
          label="Gamma"
          value={values.gamma}
          min={0.5}
          max={3}
          step={0.05}
          shown={values.gamma.toFixed(2)}
          onChange={(gamma) => change({ ...values, gamma: Math.round(gamma * 100) / 100 })}
        />
        <Slider
          label="Brightness"
          value={values.brightness}
          min={10}
          max={100}
          step={1}
          shown={`${values.brightness}%`}
          onChange={(brightness) => change({ ...values, brightness: Math.round(brightness) })}
        />
        <div className={styles.steps} data-testid="grey-steps" aria-hidden="true">
          {STEPS.map((v) => (
            <div key={v} style={{ backgroundColor: `rgb(${v}, ${v}, ${v})` }} />
          ))}
        </div>
        <p className={styles.hint}>Raise gamma until the dark grey steps on the screen look like they do here.</p>
        <div className={styles.buttons}>
          <Button type="button" variant="secondary" onClick={() => change(DEFAULTS)}>
            Reset
          </Button>
          <Button
            type="button"
            variant="secondary"
            className={styles.toggle}
            aria-pressed={patternOn}
            onClick={() => void togglePattern()}
          >
            Show test pattern
          </Button>
        </div>
      </fieldset>
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
    </>
  );
};

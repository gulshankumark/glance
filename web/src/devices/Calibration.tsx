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
 * Slider changes PUT the fields changed since the last PUT after 150 ms without change. Every successful answer newer
 * than the last applied one is applied; a failure of the latest PUT shows the server error and restores the newest
 * value the server confirmed.
 */
export const Calibration: React.FC<{ device: DeviceView; onUpdated: (device: DeviceView) => void }> = ({
  device,
  onUpdated,
}) => {
  const [values, setValues] = useState<Values>({ gamma: device.gamma, brightness: device.brightness });
  const [colourError, setColourError] = useState<string | null>(null);
  const [patternError, setPatternError] = useState<string | null>(null);
  const [patternOn, setPatternOn] = useState(false);

  const key = useRef(device.key);
  const onUpdatedRef = useRef(onUpdated);
  onUpdatedRef.current = onUpdated;
  /** The newest values the server confirmed (a PUT answer, or a device push while idle). */
  const confirmed = useRef<Values>({ gamma: device.gamma, brightness: device.brightness });
  /** Fields changed since the last PUT, waiting for the debounce. */
  const pending = useRef<Partial<Values> | null>(null);
  const debounce = useRef<number | undefined>(undefined);
  const sent = useRef(0);
  const applied = useRef(0);
  const inFlight = useRef(0);
  const patternTimer = useRef<number | undefined>(undefined);
  const patternState = useRef({ on: false, busy: false, unmounted: false });

  // Adopt values changed elsewhere (device pushes, other browsers) only while none of ours is pending or in flight,
  // so a push never moves a slider under the user. Pushes carry no version: a push older than our newest answer that
  // arrives once idle would be shown, but the service pushes after every change, so the newer push follows it.
  useEffect(() => {
    if (pending.current !== null || inFlight.current > 0) return;
    confirmed.current = { gamma: device.gamma, brightness: device.brightness };
    setValues(confirmed.current);
  }, [device.gamma, device.brightness]);

  const send = async (patch: DevicePatch) => {
    const seq = ++sent.current;
    inFlight.current++;
    try {
      // Not useDeviceUpdate: that hook keeps errors in the layout/rotation slots and neither orders answers nor
      // reports a failure, which this debounced, out-of-order-safe sender needs.
      const updated = await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(key.current)}`, patch);
      if (seq > applied.current) {
        applied.current = seq;
        confirmed.current = { gamma: updated.gamma, brightness: updated.brightness };
        // Nothing newer is waiting or under way (e.g. the latest PUT already failed): show what the server has.
        if (pending.current === null && inFlight.current === 1) setValues(confirmed.current);
        onUpdatedRef.current(updated);
      }
      if (seq === sent.current) setColourError(null);
    } catch (err) {
      if (seq !== sent.current) return;
      setColourError(errorText(err, 'The change could not be saved.'));
      if (pending.current === null) setValues(confirmed.current);
    } finally {
      inFlight.current--;
    }
  };
  const sendRef = useRef(send);
  sendRef.current = send;

  const flushPending = () => {
    window.clearTimeout(debounce.current);
    const patch = pending.current;
    pending.current = null;
    if (patch !== null) void sendRef.current(patch);
  };

  const change = (patch: Partial<Values>) => {
    setValues((v) => ({ ...v, ...patch }));
    pending.current = { ...pending.current, ...patch };
    window.clearTimeout(debounce.current);
    debounce.current = window.setTimeout(flushPending, DEBOUNCE_MS);
  };

  const showPattern = (on: boolean) => {
    window.clearTimeout(patternTimer.current);
    patternState.current.on = on;
    setPatternOn(on);
    if (on) patternTimer.current = window.setTimeout(() => showPattern(false), PATTERN_MS);
  };

  const togglePattern = async () => {
    const state = patternState.current;
    if (state.busy) return;
    const on = !state.on;
    state.busy = true;
    try {
      await setTestPattern(key.current, on);
    } catch (err) {
      if (!state.unmounted) setPatternError(errorText(err, 'The test pattern could not be changed.'));
      return;
    } finally {
      state.busy = false;
    }
    if (state.unmounted) {
      // Left the page while the POST was under way: end the pattern now.
      if (on) setTestPattern(key.current, false).catch(() => {});
      return;
    }
    setPatternError(null);
    showPattern(on);
  };

  useEffect(() => {
    // Closing or leaving the page: end a test pattern that is on with a request that outlives the page.
    const onPageHide = () => {
      if (!patternState.current.on) return;
      setTestPattern(key.current, false, { keepalive: true }).catch(() => {});
      showPattern(false);
    };
    window.addEventListener('pagehide', onPageHide);
    return () => {
      window.removeEventListener('pagehide', onPageHide);
      // Leaving the tab: send a change still waiting for its debounce, and end a test pattern that is on.
      flushPending();
      window.clearTimeout(patternTimer.current);
      const state = patternState.current;
      state.unmounted = true;
      if (state.on) setTestPattern(key.current, false).catch(() => {});
    };
  }, []);

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
          onChange={(gamma) => change({ gamma: Math.round(gamma * 100) / 100 })}
        />
        <Slider
          label="Brightness"
          value={values.brightness}
          min={10}
          max={100}
          step={1}
          shown={`${values.brightness}%`}
          onChange={(brightness) => change({ brightness: Math.round(brightness) })}
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
        {patternError && (
          <p className={styles.patternError} role="alert">
            {patternError}
          </p>
        )}
      </fieldset>
      {colourError && (
        <p className={styles.error} role="alert">
          {colourError}
        </p>
      )}
    </>
  );
};

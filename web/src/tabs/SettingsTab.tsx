import React, { useCallback, useEffect, useId, useRef, useState } from 'react';
import { api, ApiError, getBlob } from '../api/client';
import type { AuthState, Settings } from '../api/types';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import styles from './SettingsTab.module.css';

const PROJECT_URL = 'https://github.com/gulshankumark/glance';
const RELOAD_DELAY_MS = 1500;
const FPS_PRESETS = [8, 12, 15, 20, 24];
const FPS_OPTIONS = [
  { value: 'off', label: 'Off' },
  ...FPS_PRESETS.map((n) => ({ value: String(n), label: `${n} fps` })),
  { value: 'custom', label: 'Custom' },
];

/** A readable message for a failed request; `byStatus` replaces the server's text for given statuses. */
function errorText(err: unknown, byStatus: Record<number, string> = {}): string {
  if (err instanceof ApiError) {
    const text = byStatus[err.status] ?? err.errors.join(', ');
    return text.charAt(0).toUpperCase() + text.slice(1);
  }
  return 'Could not reach the Glance service.';
}

/** A whole number within min..max parsed from user text, or null. */
function wholeNumber(text: string, min: number, max: number): number | null {
  if (!/^\d+$/.test(text.trim())) return null;
  const n = Number(text);
  return n >= min && n <= max ? n : null;
}

const ErrorText: React.FC<{ text: string | null }> = ({ text }) =>
  text ? (
    <p role="alert" className={styles.error}>
      {text}
    </p>
  ) : null;

const Check: React.FC<{
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  hint?: string;
}> = ({ label, checked, onChange, disabled, hint }) => {
  const id = useId();
  return (
    <div className={styles.check}>
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        aria-describedby={hint ? `${id}-hint` : undefined}
        onChange={(e) => onChange(e.target.checked)}
      />
      <div>
        <label htmlFor={id}>{label}</label>
        {hint && (
          <span id={`${id}-hint`} className={styles.checkHint}>
            {hint}
          </span>
        )}
      </div>
    </div>
  );
};

const DisplaySection: React.FC<{ settings: Settings; onSaved: (s: Settings) => void }> = ({ settings, onSaved }) => {
  const stored = settings.maxOverlayAnimationFps;
  const isPreset = stored === null || FPS_PRESETS.includes(stored);
  const [unit, setUnit] = useState(settings.unit);
  const [clock24h, setClock24h] = useState(settings.clock24h);
  const [fpsChoice, setFpsChoice] = useState(stored === null ? 'off' : isPreset ? String(stored) : 'custom');
  const [customFps, setCustomFps] = useState(isPreset ? '' : String(stored));
  const [customError, setCustomError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);

  const edited =
    <T,>(set: (v: T) => void) =>
    (v: T) => {
      set(v);
      setSaved(false);
    };

  const save = async () => {
    setError(null);
    setSaved(false);
    let fps: number | null = null;
    if (fpsChoice === 'custom') {
      fps = wholeNumber(customFps, 1, 60);
      if (fps === null) {
        setCustomError('Enter a whole number from 1 to 60.');
        return;
      }
    } else if (fpsChoice !== 'off') {
      fps = Number(fpsChoice);
    }
    setCustomError(null);
    setSaving(true);
    try {
      onSaved(await api<Settings>('PUT', '/api/settings', { unit, clock24h, maxOverlayAnimationFps: fps }));
      setSaved(true);
    } catch (err) {
      setError(errorText(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Card title="Display" role="region" aria-label="Display">
      <Field label="Temperature unit">
        <Select
          value={unit}
          onChange={(e) => edited(setUnit)(e.target.value as Settings['unit'])}
          options={[
            { value: 'celsius', label: '°C (Celsius)' },
            { value: 'fahrenheit', label: '°F (Fahrenheit)' },
          ]}
        />
      </Field>
      <Check label="24-hour clock" checked={clock24h} onChange={edited(setClock24h)} />
      <Field
        label="Frame-rate cap for animations with text or gauges"
        hint="Lower values use less CPU; animation speed stays the same"
      >
        <Select value={fpsChoice} onChange={(e) => edited(setFpsChoice)(e.target.value)} options={FPS_OPTIONS} />
      </Field>
      {fpsChoice === 'custom' && (
        <Field label="Custom frame rate (1–60 fps)" error={customError ?? undefined}>
          <input
            className={styles.input}
            type="number"
            inputMode="numeric"
            min={1}
            max={60}
            step={1}
            value={customFps}
            onChange={(e) => {
              edited(setCustomFps)(e.target.value);
              setCustomError(null);
            }}
          />
        </Field>
      )}
      <div className={styles.actions}>
        <Button onClick={save} disabled={saving}>
          Save
        </Button>
        {saved && (
          <span role="status" className={styles.ok}>
            Saved
          </span>
        )}
      </div>
      <ErrorText text={error} />
    </Card>
  );
};

type PasswordErrors = { current?: string; next?: string; confirm?: string; form?: string };

const PasswordForm: React.FC<{ hasPassword: boolean; onSaved: () => void }> = ({ hasPassword, onSaved }) => {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errors, setErrors] = useState<PasswordErrors>({});
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaved(false);
    let problem: PasswordErrors = {};
    if (hasPassword && !current) problem = { current: 'Enter your current password.' };
    else if (next.length < 8) problem = { next: 'Use at least 8 characters.' };
    else if (next !== confirm) problem = { confirm: 'The passwords do not match.' };
    setErrors(problem);
    if (Object.keys(problem).length > 0) return;

    setSaving(true);
    try {
      await api('PUT', '/api/settings/password', hasPassword ? { current, new: next } : { new: next });
      setCurrent('');
      setNext('');
      setConfirm('');
      setSaved(true);
      onSaved();
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) setErrors({ current: 'The current password is wrong.' });
      else setErrors({ form: errorText(err, { 429: 'Too many attempts. Wait a minute and try again.' }) });
    } finally {
      setSaving(false);
    }
  };

  const input = (value: string, set: (v: string) => void, autoComplete: string) => (
    <input
      className={styles.input}
      type="password"
      autoComplete={autoComplete}
      value={value}
      onChange={(e) => {
        set(e.target.value);
        setSaved(false);
      }}
    />
  );

  return (
    <form className={styles.subSection} onSubmit={submit} noValidate>
      <h3 className={styles.subTitle}>Password</h3>
      <p className={styles.muted} style={{ marginBottom: 'var(--space-3)' }}>
        {hasPassword
          ? 'Needed to sign in from other devices. Changing it signs out every other session.'
          : 'No password is set. Other devices can only connect once one is set.'}
      </p>
      {hasPassword && (
        <Field label="Current password" error={errors.current}>
          {input(current, setCurrent, 'current-password')}
        </Field>
      )}
      <Field label="New password" hint="At least 8 characters." error={errors.next}>
        {input(next, setNext, 'new-password')}
      </Field>
      <Field label="Confirm new password" error={errors.confirm}>
        {input(confirm, setConfirm, 'new-password')}
      </Field>
      <div className={styles.actions}>
        <Button type="submit" disabled={saving}>
          {hasPassword ? 'Change password' : 'Set password'}
        </Button>
        {saved && (
          <span role="status" className={styles.ok}>
            Password saved
          </span>
        )}
      </div>
      <ErrorText text={errors.form ?? null} />
    </form>
  );
};

/** The address the UI is served at for the given network settings (current host, or localhost). */
function addressFor(https: boolean, port: number): string {
  const host = window.location.hostname || 'localhost';
  return `${https ? 'https' : 'http'}://${host}:${port}/#settings`;
}

type NetworkResult = Pick<Settings, 'networkAccess' | 'https' | 'port'>;

const NetworkForm: React.FC<{ settings: Settings; onSaved: (s: Settings) => void }> = ({ settings, onSaved }) => {
  const [enabled, setEnabled] = useState(settings.networkAccess);
  const [https, setHttps] = useState(settings.https);
  const [port, setPort] = useState(String(settings.port));
  const [portError, setPortError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [reloading, setReloading] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const edited =
    <T,>(set: (v: T) => void) =>
    (v: T) => {
      set(v);
      setSaved(false);
    };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSaved(false);
    const portNumber = wholeNumber(port, 1024, 65535);
    if (portNumber === null) {
      setPortError('Use a port from 1024 to 65535.');
      return;
    }
    setPortError(null);
    setSaving(true);
    try {
      const result = await api<NetworkResult>('PUT', '/api/settings/network', { enabled, https, port: portNumber });
      onSaved({ ...settings, ...result });
      // Only a new scheme or port moves the UI; navigate after the user has had a moment to read why.
      if (result.https !== settings.https || result.port !== settings.port) {
        setReloading(true);
        const target = addressFor(result.https, result.port);
        timer.current = window.setTimeout(() => window.location.assign(target), RELOAD_DELAY_MS);
      } else {
        setSaved(true);
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setPortError('Port is in use by another program. Choose a different port.');
      } else {
        setError(errorText(err));
      }
    } finally {
      setSaving(false);
    }
  };

  const toggleLocked = !settings.hasPassword && !enabled;

  return (
    <form className={styles.subSection} onSubmit={submit} noValidate>
      <h3 className={styles.subTitle}>Network</h3>
      <Check
        label="Allow access from other devices on the network"
        checked={enabled}
        disabled={toggleLocked}
        hint={toggleLocked ? 'Set a password first to allow network access.' : 'Other devices must sign in with the password.'}
        onChange={edited(setEnabled)}
      />
      <Check
        label="Use HTTPS"
        checked={https}
        hint="Encrypts the connection; browsers may warn about the certificate."
        onChange={edited(setHttps)}
      />
      <Field label="Port" hint="1024 to 65535." error={portError ?? undefined}>
        <input
          className={styles.input}
          type="text"
          inputMode="numeric"
          value={port}
          onChange={(e) => {
            edited(setPort)(e.target.value);
            setPortError(null);
          }}
        />
      </Field>
      <div className={styles.actions}>
        <Button type="submit" disabled={saving || reloading}>
          Save network settings
        </Button>
        {saved && (
          <span role="status" className={styles.ok}>
            Network settings saved
          </span>
        )}
      </div>
      {reloading && (
        <Banner variant="info" style={{ marginTop: 'var(--space-3)' }}>
          The page will reload at the new address
        </Banner>
      )}
      <ErrorText text={error} />
    </form>
  );
};

const LogoutButton: React.FC<{ navigate: (hash: string) => void }> = ({ navigate }) => {
  const [error, setError] = useState<string | null>(null);
  const logout = async () => {
    setError(null);
    try {
      await api('POST', '/api/auth/logout');
      navigate('#login');
    } catch (err) {
      setError(errorText(err));
    }
  };
  return (
    <div className={styles.subSection}>
      <h3 className={styles.subTitle}>Session</h3>
      <Button variant="secondary" onClick={logout}>
        Log out
      </Button>
      <ErrorText text={error} />
    </div>
  );
};

const LogsSection: React.FC = () => {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const download = async () => {
    setError(null);
    setBusy(true);
    try {
      const url = URL.createObjectURL(await getBlob('/api/logs'));
      const link = document.createElement('a');
      link.href = url;
      link.download = 'glance-logs.zip';
      link.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card title="Logs" role="region" aria-label="Logs">
      <p className={styles.muted} style={{ marginBottom: 'var(--space-3)' }}>
        The service's log files as a zip archive, for troubleshooting or a bug report.
      </p>
      <Button variant="secondary" onClick={download} disabled={busy}>
        Download logs
      </Button>
      <ErrorText text={error} />
    </Card>
  );
};

const AboutSection: React.FC = () => {
  const [version, setVersion] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    api<{ version: string }>('GET', '/api/health').then(
      (h) => setVersion(h.version),
      () => setFailed(true),
    );
  }, []);

  return (
    <Card title="About" role="region" aria-label="About" className={styles.about}>
      <p>Glance{version ? ` — Version ${version}` : failed ? ' — version unavailable' : ''}</p>
      <p>Free software under the GPL-3.0 licence.</p>
      <p>
        <a href={PROJECT_URL} target="_blank" rel="noopener noreferrer">
          Project home and source code
        </a>
      </p>
    </Card>
  );
};

export const SettingsTab: React.FC<{ navigate: (hash: string) => void }> = ({ navigate }) => {
  const [settings, setSettings] = useState<Settings | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [auth, setAuth] = useState<AuthState | null>(null);

  const load = useCallback(() => {
    setLoadError(null);
    api<Settings>('GET', '/api/settings').then(setSettings, (err) => setLoadError(errorText(err)));
  }, []);

  useEffect(() => {
    load();
    api<AuthState>('GET', '/api/auth/state').then(setAuth, () => setAuth(null));
  }, [load]);

  return (
    <div>
      <h2 style={{ marginBottom: 'var(--space-4)' }}>Settings</h2>
      {loadError && (
        <div style={{ marginBottom: 'var(--space-4)' }}>
          <ErrorText text={`Could not load the settings: ${loadError}`} />
          <Button variant="secondary" onClick={load} style={{ marginTop: 'var(--space-2)' }}>
            Try again
          </Button>
        </div>
      )}
      {!settings && !loadError && <p className={styles.muted}>Loading…</p>}
      <div className={styles.grid}>
        <div>
          {settings && <DisplaySection settings={settings} onSaved={setSettings} />}
          <LogsSection />
          <AboutSection />
        </div>
        <div>
          {settings && (
            <Card title="Access" role="region" aria-label="Access">
              <PasswordForm
                hasPassword={settings.hasPassword}
                onSaved={() => setSettings((s) => (s ? { ...s, hasPassword: true } : s))}
              />
              <NetworkForm settings={settings} onSaved={setSettings} />
              {auth?.authenticated && !auth.loopback && <LogoutButton navigate={navigate} />}
            </Card>
          )}
        </div>
      </div>
    </div>
  );
};

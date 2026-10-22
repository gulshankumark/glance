import React, { useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { DeviceView, Layout, LayoutSummary } from '../api/types';
import { Canvas } from '../editor/Canvas';
import { newLayout } from '../editor/model';
import { useDevices } from '../live/LiveProvider';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Dialog } from '../ui/Dialog';
import { Field } from '../ui/Field';
import { Select } from '../ui/Select';
import styles from './LayoutsTab.module.css';

const FALLBACK_SIZE = { width: 1280, height: 480 };

const layoutPath = (name: string) => `/api/layouts/${encodeURIComponent(name)}`;
const errorText = (e: unknown, fallback: string) => (e instanceof ApiError ? e.errors.join('; ') : fallback);

/** The size a layout must have to fill the device as rotated (width and height swap for 90° and 270°). */
function canvasSize(d: Pick<DeviceView, 'width' | 'height' | 'rotation'>): { width: number; height: number } {
  return d.rotation === 90 || d.rotation === 270 ? { width: d.height, height: d.width } : { width: d.width, height: d.height };
}

/** Client-side check of the server's layout name rule (docs/api.md); the server stays the authority. */
function nameProblem(name: string): string | null {
  if (!/^[A-Za-z0-9 _-]{1,64}$/.test(name)) return 'Use 1–64 letters, digits, spaces, _ or -.';
  if (name.endsWith(' ')) return 'The name must not end with a space.';
  return null;
}

const sameName = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'base' }) === 0;

function unusedName(summaries: LayoutSummary[]): string {
  for (let n = 1; ; n++) {
    const name = `New layout ${n}`;
    if (!summaries.some((s) => sameName(s.name, name))) return name;
  }
}

interface Doc {
  /** Name of the stored layout this draft edits; null for a new layout that was never saved. */
  savedName: string | null;
  draft: Layout;
  /** JSON of the stored version, to tell whether the draft has unsaved changes. */
  saved: string;
}

type DialogState =
  | { kind: 'duplicate' | 'rename'; name: string; error: string | null }
  | { kind: 'delete'; error: string | null }
  | { kind: 'activate'; deviceKey: string; error: string | null };

const dialogTexts: Record<DialogState['kind'], { title: (name: string) => string; action: string }> = {
  duplicate: { title: (n) => `Duplicate ${n}`, action: 'Duplicate' },
  rename: { title: (n) => `Rename ${n}`, action: 'Rename' },
  delete: { title: (n) => `Delete ${n}?`, action: 'Delete' },
  activate: { title: (n) => `Show ${n} on…`, action: 'Make active' },
};

export const LayoutsTab: React.FC<{ navigate: (hash: string) => void; route?: string }> = ({ route }) => {
  const liveDevices = useDevices();
  const [summaries, setSummaries] = useState<LayoutSummary[] | null>(null);
  const [devices, setDevices] = useState<DeviceView[] | null>(null);
  const [doc, setDoc] = useState<Doc | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [saveErrors, setSaveErrors] = useState<string[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [busy, setBusy] = useState(false);
  /** Changes whenever another layout is opened, so the canvas starts without the previous layout's preview. */
  const [openCount, setOpenCount] = useState(0);
  const loaded = useRef<Promise<{ layouts: LayoutSummary[]; devices: DeviceView[] }> | null>(null);

  if (!loaded.current) {
    loaded.current = Promise.all([
      api<LayoutSummary[]>('GET', '/api/layouts').catch(() => [] as LayoutSummary[]),
      api<DeviceView[]>('GET', '/api/devices').catch(() => [] as DeviceView[]),
    ]).then(([layouts, list]) => ({ layouts, devices: list }));
  }

  const reloadSummaries = async () => {
    try {
      setSummaries(await api<LayoutSummary[]>('GET', '/api/layouts'));
    } catch {
      // keep the list we have; a 401 is handled by the client
    }
  };

  useEffect(() => {
    let cancelled = false;
    loaded.current!.then(({ layouts, devices: list }) => {
      if (cancelled) return;
      setSummaries(layouts);
      setDevices((prev) => prev ?? list);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // The event stream always carries the full current list.
  useEffect(() => {
    if (liveDevices) setDevices(liveDevices);
  }, [liveDevices]);

  const open = useCallback((next: Doc) => {
    setDoc(next);
    setOpenCount((n) => n + 1);
    setSelectedId(null);
    setSaveErrors([]);
    setNotice(null);
  }, []);

  const startNew = useCallback(
    (layouts: LayoutSummary[], devs: DeviceView[]) => {
      const size = devs.length > 0 ? canvasSize(devs[0]) : FALLBACK_SIZE;
      open({ savedName: null, draft: newLayout(unusedName(layouts), size.width, size.height), saved: '' });
    },
    [open],
  );

  // "#layouts/new" (the Display tab's "Create a layout") opens a new unsaved layout once the lists are known.
  useEffect(() => {
    if (route !== '#layouts/new') return;
    let cancelled = false;
    loaded.current!.then(({ layouts, devices: list }) => {
      if (!cancelled) startNew(layouts, list);
    });
    return () => {
      cancelled = true;
    };
  }, [route, startNew]);

  const openLayout = async (name: string) => {
    try {
      const layout = await api<Layout>('GET', layoutPath(name));
      open({ savedName: name, draft: layout, saved: JSON.stringify(layout) });
    } catch (e) {
      setNotice(errorText(e, `Layout ${name} could not be opened.`));
    }
  };

  const dirty = doc !== null && (doc.savedName === null || JSON.stringify(doc.draft) !== doc.saved);

  const save = async () => {
    if (!doc) return;
    const { draft } = doc;
    if (doc.savedName === null && summaries?.some((s) => sameName(s.name, draft.name))) {
      setSaveErrors([`A layout named ${draft.name} already exists; rename this one first.`]);
      return;
    }
    setBusy(true);
    try {
      await api('PUT', layoutPath(draft.name), draft);
      // Edits made while the request ran stay unsaved: only the version that was sent counts as stored.
      setDoc((d) => d && { ...d, savedName: draft.name, saved: JSON.stringify(draft) });
      setSaveErrors([]);
      await reloadSummaries();
    } catch (e) {
      setSaveErrors(e instanceof ApiError ? e.errors : ['The layout could not be saved.']);
    } finally {
      setBusy(false);
    }
  };

  const activate = async (deviceKey: string) => {
    if (!doc?.savedName) return;
    const updated = await api<DeviceView>('PUT', `/api/devices/${encodeURIComponent(deviceKey)}`, {
      activeLayout: doc.savedName,
    });
    setDevices((list) => list?.map((d) => (d.key === updated.key ? updated : d)) ?? list);
  };

  const onMakeActive = async () => {
    if (!devices?.length) return;
    if (devices.length > 1) {
      setDialog({ kind: 'activate', deviceKey: devices[0].key, error: null });
      return;
    }
    setNotice(null);
    setBusy(true);
    try {
      await activate(devices[0].key);
    } catch (e) {
      setNotice(errorText(e, 'The layout could not be made active.'));
    } finally {
      setBusy(false);
    }
  };

  /** Renames the stored layout (there is no rename route: copy, move the screens showing it, remove the old one). */
  const rename = async (current: Doc, name: string) => {
    const old = current.savedName;
    if (old !== null) {
      await api('POST', `${layoutPath(old)}/duplicate`, { newName: name });
      for (const d of devices ?? []) {
        if (d.activeLayout === old) {
          await api('PUT', `/api/devices/${encodeURIComponent(d.key)}`, { activeLayout: name });
        }
      }
      await api('DELETE', layoutPath(old));
    }
    setDoc((d) =>
      d && {
        savedName: old === null ? null : name,
        draft: { ...d.draft, name },
        saved: old === null ? '' : JSON.stringify({ ...(JSON.parse(d.saved) as Layout), name }),
      },
    );
  };

  const confirmDialog = async () => {
    if (!dialog || !doc) return;
    if (dialog.kind === 'duplicate' || dialog.kind === 'rename') {
      const problem = nameProblem(dialog.name);
      if (problem) {
        setDialog({ ...dialog, error: problem });
        return;
      }
    }
    setBusy(true);
    try {
      if (dialog.kind === 'activate') {
        await activate(dialog.deviceKey);
      } else if (dialog.kind === 'delete') {
        if (doc.savedName !== null) await api('DELETE', layoutPath(doc.savedName));
        setDoc(null);
        setSelectedId(null);
      } else if (dialog.kind === 'duplicate' && doc.savedName !== null) {
        await api('POST', `${layoutPath(doc.savedName)}/duplicate`, { newName: dialog.name });
      } else if (dialog.kind === 'rename' && dialog.name !== doc.draft.name) {
        await rename(doc, dialog.name);
      }
      setDialog(null);
    } catch (e) {
      setDialog({ ...dialog, error: errorText(e, 'That did not work.') });
    } finally {
      setBusy(false);
      if (dialog.kind !== 'activate') await reloadSummaries();
    }
  };

  // The screen showing this layout, else the first screen: its rotated canvas is what the layout should match.
  const target = devices?.find((d) => doc?.savedName != null && d.activeLayout === doc.savedName) ?? devices?.[0];
  const targetSize = target ? canvasSize(target) : null;
  const mismatch =
    doc !== null && targetSize !== null && (targetSize.width !== doc.draft.width || targetSize.height !== doc.draft.height);

  return (
    <div className={styles.container}>
      <Card style={{ marginBottom: 0 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h2 style={{ fontSize: '1.1rem' }}>Layouts</h2>
          <Button
            variant="secondary"
            disabled={summaries === null}
            onClick={() => summaries && startNew(summaries, devices ?? [])}
          >
            + New
          </Button>
        </div>
        {summaries === null && <p style={{ color: 'var(--muted)', marginTop: 'var(--space-3)' }}>Loading…</p>}
        {summaries?.length === 0 && (
          <p style={{ color: 'var(--muted)', marginTop: 'var(--space-3)' }}>No layouts yet.</p>
        )}
        <ul className={styles.list}>
          {summaries?.map((s) => (
            <li key={s.name} className={styles.listItem}>
              <button
                type="button"
                className={styles.listButton}
                aria-current={doc?.savedName === s.name}
                disabled={s.error !== null}
                title={s.error ?? undefined}
                onClick={() => openLayout(s.name)}
              >
                {s.name}
              </button>
              <span className={styles.listMeta}>
                {s.error !== null ? 'cannot be loaded' : `${s.width}×${s.height}`}
                {devices?.some((d) => d.activeLayout === s.name) && ' · active'}
              </span>
            </li>
          ))}
        </ul>
      </Card>

      <Card style={{ marginBottom: 0, minWidth: 0 }}>
        {notice && (
          <Banner variant="danger" style={{ marginBottom: 'var(--space-3)' }}>
            {notice}
          </Banner>
        )}
        {!doc ? (
          <p style={{ color: 'var(--muted)' }}>Choose a layout, or start a new one.</p>
        ) : (
          <>
            <div className={styles.toolbar}>
              <span className={styles.title} data-testid="layout-title">
                {doc.draft.name} · {doc.draft.width}×{doc.draft.height}
              </span>
              {dirty && (
                <span className={styles.unsaved} data-testid="unsaved-marker">
                  ● Unsaved changes
                </span>
              )}
              <Button onClick={save} disabled={busy}>
                Save
              </Button>
              <Button
                variant="secondary"
                disabled={busy || doc.savedName === null || !devices?.length}
                title={doc.savedName === null ? 'Save the layout first' : 'Shows the saved version on a screen'}
                onClick={onMakeActive}
              >
                Make active
              </Button>
              <Button
                variant="secondary"
                disabled={busy || doc.savedName === null}
                onClick={() => setDialog({ kind: 'duplicate', name: `${doc.draft.name} copy`.slice(0, 64), error: null })}
              >
                Duplicate
              </Button>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => setDialog({ kind: 'rename', name: doc.draft.name, error: null })}
              >
                Rename
              </Button>
              <Button variant="danger" disabled={busy} onClick={() => setDialog({ kind: 'delete', error: null })}>
                Delete
              </Button>
            </div>

            {saveErrors.length > 0 && (
              <div className={styles.errors} role="alert" data-testid="save-errors">
                <strong>Not saved:</strong>
                <ul>
                  {saveErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            )}

            {mismatch && target && targetSize && (
              <Banner data-testid="size-mismatch" variant="warning" style={{ marginBottom: 'var(--space-3)' }}>
                Made for {doc.draft.width}×{doc.draft.height} — {target.name} shows {targetSize.width}×{targetSize.height}.
              </Banner>
            )}

            <Canvas
              key={openCount}
              layout={doc.draft}
              selectedId={selectedId}
              onSelect={setSelectedId}
              onChange={(draft) => setDoc((d) => d && { ...d, draft })}
            />
          </>
        )}
      </Card>

      {dialog && doc && (
        <Dialog open title={dialogTexts[dialog.kind].title(doc.draft.name)} onClose={() => !busy && setDialog(null)}>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void confirmDialog();
            }}
          >
            {(dialog.kind === 'duplicate' || dialog.kind === 'rename') && (
              <Field label="Name" error={dialog.error ?? undefined}>
                <input
                  className={styles.input}
                  value={dialog.name}
                  maxLength={64}
                  autoFocus
                  onChange={(e) => setDialog({ ...dialog, name: e.target.value, error: null })}
                />
              </Field>
            )}
            {dialog.kind === 'delete' && (
              <p>
                {doc.savedName === null
                  ? 'This new layout has not been saved; it will be discarded.'
                  : 'The layout file is removed. This cannot be undone.'}
              </p>
            )}
            {dialog.kind === 'activate' && (
              <Field label="Screen">
                <Select value={dialog.deviceKey} onChange={(e) => setDialog({ ...dialog, deviceKey: e.target.value })}>
                  {devices?.map((d) => (
                    <option key={d.key} value={d.key}>
                      {d.name} ({d.serial})
                    </option>
                  ))}
                </Select>
              </Field>
            )}
            {(dialog.kind === 'delete' || dialog.kind === 'activate') && dialog.error && (
              <p role="alert" style={{ color: 'var(--danger)', marginTop: 'var(--space-2)' }}>
                {dialog.error}
              </p>
            )}
            <div className={styles.dialogActions}>
              <Button type="button" variant="secondary" disabled={busy} onClick={() => setDialog(null)}>
                Cancel
              </Button>
              <Button type="submit" variant={dialog.kind === 'delete' ? 'danger' : 'primary'} disabled={busy}>
                {dialogTexts[dialog.kind].action}
              </Button>
            </div>
          </form>
        </Dialog>
      )}
    </div>
  );
};

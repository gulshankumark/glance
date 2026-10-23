import React, { useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { DeviceView, Item, Layout, LayoutSummary } from '../api/types';
import { AddMenu } from '../editor/AddMenu';
import { IMAGE_KINDS } from '../editor/assets';
import { AssetImport } from '../editor/AssetImport';
import { BackgroundPicker } from '../editor/BackgroundPicker';
import { Canvas } from '../editor/Canvas';
import editorStyles from '../editor/Editor.module.css';
import { describeItem } from '../editor/catalog';
import { ItemList } from '../editor/ItemList';
import { addItem, newLayout, removeItem, sensorItem, updateItem } from '../editor/model';
import { Properties } from '../editor/Properties';
import { SensorTree } from '../editor/SensorTree';
import { clearStash, readStash, writeStash } from '../editor/draftStash';
import { setNavBlocker } from '../lib/navGuard';
import { useDirty } from '../editor/useDirty';
import { useEditorData } from '../editor/useEditorData';
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

/** One sentence naming the screens whose active layout is `layout` (deleting it sends them to the status screen). */
const ScreensShowing: React.FC<{ devices: DeviceView[] | null; layout: string }> = ({ devices, layout }) => {
  const names = (devices ?? []).filter((d) => d.activeLayout === layout).map((d) => `${d.name} (${d.serial})`);
  if (names.length === 0) return null;
  return (
    <p>
      {names.join(', ')} {names.length === 1 ? 'shows' : 'show'} this layout and will show the built-in status screen
      instead.
    </p>
  );
};

/**
 * A page load straight into the Layouts tab follows a confirmed "leave page" prompt (reload, closed tab): its stash
 * is dropped. Loads elsewhere (e.g. the login page after a 401) and in-app navigation keep it.
 */
let dropStashOnFirstMount = window.location.hash.startsWith('#layouts');

/** Restores a draft stashed before leaving the editor (e.g. for the login page after a 401). */
function restoredDoc(): Doc | null {
  if (dropStashOnFirstMount) {
    dropStashOnFirstMount = false;
    clearStash();
    return null;
  }
  const s = readStash();
  return s && { savedName: s.layoutName, draft: s.draft, saved: s.savedVersion };
}

export const LayoutsTab: React.FC<{ navigate: (hash: string) => void; route?: string }> = ({ navigate, route }) => {
  const liveDevices = useDevices();
  const [summaries, setSummaries] = useState<LayoutSummary[] | null>(null);
  const [devices, setDevices] = useState<DeviceView[] | null>(null);
  const [doc, setDoc] = useState<Doc | null>(restoredDoc);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [saveErrors, setSaveErrors] = useState<string[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [busy, setBusy] = useState(false);
  /** The action waiting for the user to save or discard unsaved edits (Save / Discard / Cancel dialog). */
  const [pending, setPending] = useState<{ run: () => void; errors: string[] } | null>(null);
  /** Changes whenever another layout is opened, so the canvas starts without the previous layout's preview. */
  const [openCount, setOpenCount] = useState(0);
  const openCountRef = useRef(openCount);
  openCountRef.current = openCount;
  const editorData = useEditorData();
  const { catalog, assets } = editorData;
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
    setSelectedIds([]);
    setSaveErrors([]);
    setNotice(null);
  }, []);

  // Opening a layout or starting a new one: only the latest of these wins (stale answers are dropped).
  const openSeq = useRef(0);
  const openAbort = useRef<AbortController | null>(null);
  const nextOpen = () => {
    openAbort.current?.abort();
    openAbort.current = new AbortController();
    return { seq: ++openSeq.current, signal: openAbort.current.signal };
  };
  const summariesRef = useRef(summaries);
  summariesRef.current = summaries;
  const devicesRef = useRef(devices);
  devicesRef.current = devices;

  /** A new unsaved layout named after the current layout list, sized for the first connected screen. */
  const startNew = useCallback(async () => {
    const { seq, signal } = nextOpen();
    const [layouts, devs] = await Promise.all([
      api<LayoutSummary[]>('GET', '/api/layouts', undefined, { signal }).catch(() => null),
      api<DeviceView[]>('GET', '/api/devices', undefined, { signal }).catch(() => null),
    ]);
    if (seq !== openSeq.current) return;
    if (layouts) setSummaries(layouts);
    const screen = (devs ?? devicesRef.current ?? []).find(
      (d) => d.status === 'connected' && d.width > 0 && d.height > 0,
    );
    const size = screen ? canvasSize(screen) : FALLBACK_SIZE;
    const name = unusedName(layouts ?? summariesRef.current ?? []);
    open({ savedName: null, draft: newLayout(name, size.width, size.height), saved: '' });
  }, [open]);

  // "#layouts/new" (the Display tab's "Create a layout") opens a new unsaved layout; the URL becomes "#layouts" so
  // a reload or Back does not start another one (the hashchange event keeps the app's route in step).
  useEffect(() => {
    if (route !== '#layouts/new') return;
    window.history.replaceState(null, '', '#layouts');
    window.dispatchEvent(new HashChangeEvent('hashchange'));
    guardRef.current(() => void startNew());
  }, [route, startNew]);

  const openLayout = async (name: string) => {
    const { seq, signal } = nextOpen();
    try {
      const layout = await api<Layout>('GET', layoutPath(name), undefined, { signal });
      if (seq === openSeq.current) open({ savedName: name, draft: layout, saved: JSON.stringify(layout) });
    } catch (e) {
      if (seq === openSeq.current) setNotice(errorText(e, `Layout ${name} could not be opened.`));
    }
  };

  /** Adds an item on top of the draft and selects it. */
  const insert = (item: Item) => {
    if (!doc) return;
    // The free id is picked against the shown draft so it can be selected; addItem checks it again on the latest one.
    const { items } = addItem(doc.draft, item);
    const { id } = items[items.length - 1];
    setDoc((d) => d && { ...d, draft: addItem(d.draft, { ...item, id }) });
    setSelectedIds([id]);
  };

  // In the image pickers: imported images join the list, where they can be picked.
  const imageImport = <AssetImport kinds={IMAGE_KINDS} reloadAssets={editorData.reloadAssets} onImported={() => undefined} />;

  // The selection never names items the draft no longer has (removed, undone by Discard, another layout opened).
  const draftItems = doc?.draft.items;
  useEffect(() => {
    const present = new Set(draftItems?.map((i) => i.id));
    setSelectedIds((ids) => (ids.every((id) => present.has(id)) ? ids : ids.filter((id) => present.has(id))));
  }, [draftItems]);

  const selected =
    selectedIds.length === 1 ? (doc?.draft.items.find((i) => i.id === selectedIds[0]) ?? null) : null;

  const dirty = useDirty(doc?.draft ?? null, doc?.saved ?? '', doc?.savedName === null);

  // Every edit is stashed, so leaving without a prompt (a 401, Back) loses nothing; a clean editor clears it.
  useEffect(() => {
    if (doc && dirty) writeStash({ layoutName: doc.savedName, draft: doc.draft, savedVersion: doc.saved });
    else clearStash();
  }, [doc, dirty]);

  // Closing or reloading the page with unsaved edits asks first.
  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);

  /** Runs `action` now, or after the user saved or discarded the unsaved edits. */
  const guard = (action: () => void) => {
    if (dirty) setPending({ run: action, errors: [] });
    else action();
  };
  const guardRef = useRef(guard);
  guardRef.current = guard;

  // Back, Forward or a typed hash with unsaved edits asks first. The login page (a 401) is never held back: the
  // draft is stashed and restored afterwards.
  useEffect(() => {
    if (!dirty) return;
    setNavBlocker((hash) => {
      if (hash.startsWith('#layouts') || hash.startsWith('#login')) return false;
      setPending({ run: () => navigate(hash), errors: [] });
      return true;
    });
    return () => setNavBlocker(null);
  }, [dirty, navigate]);

  // Switching tab through the app's links with unsaved edits asks first.
  useEffect(() => {
    if (!dirty) return;
    const onClick = (e: MouseEvent) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const href = (e.target as Element | null)?.closest?.('a[href^="#"]')?.getAttribute('href');
      if (!href || href.startsWith('#layouts')) return;
      e.preventDefault();
      setPending({ run: () => navigate(href), errors: [] });
    };
    document.addEventListener('click', onClick, true);
    return () => document.removeEventListener('click', onClick, true);
  }, [dirty, navigate]);

  /** Saves the draft; returns the problems (empty when it was saved). */
  const save = async (): Promise<string[]> => {
    if (!doc) return [];
    const { draft } = doc;
    if (doc.savedName === null && summaries?.some((s) => sameName(s.name, draft.name))) {
      const errors = [`A layout named ${draft.name} already exists; rename this one first.`];
      setSaveErrors(errors);
      return errors;
    }
    setBusy(true);
    try {
      await api('PUT', layoutPath(draft.name), draft);
      // Edits made while the request ran stay unsaved: only the version that was sent counts as stored.
      setDoc((d) => d && { ...d, savedName: draft.name, saved: JSON.stringify(draft) });
      setSaveErrors([]);
      await reloadSummaries();
      return [];
    } catch (e) {
      const errors = e instanceof ApiError ? e.errors : ['The layout could not be saved.'];
      setSaveErrors(errors);
      return errors;
    } finally {
      setBusy(false);
    }
  };

  const saveThenContinue = async () => {
    if (!pending) return;
    const errors = await save();
    if (errors.length > 0) {
      setPending({ ...pending, errors });
      return;
    }
    setPending(null);
    setNavBlocker(null);
    pending.run();
  };

  const discardThenContinue = () => {
    if (!pending || !doc) return;
    clearStash();
    setDoc(doc.savedName === null ? null : { ...doc, draft: JSON.parse(doc.saved) as Layout });
    setSelectedIds([]);
    setSaveErrors([]);
    setPending(null);
    setNavBlocker(null);
    pending.run();
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
        setSelectedIds([]);
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
            onClick={() => guard(() => void startNew())}
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
                onClick={() => guard(() => void openLayout(s.name))}
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
              <Button onClick={() => void save()} disabled={busy}>
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
                onClick={() =>
                  guard(() => setDialog({ kind: 'duplicate', name: `${doc.draft.name} copy`.slice(0, 64), error: null }))
                }
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

            <AddMenu layout={doc.draft} catalog={catalog} assets={assets} onInsert={insert} imageImport={imageImport} />

            <Canvas
              key={openCount}
              layout={doc.draft}
              selectedIds={selectedIds}
              onSelect={setSelectedIds}
              onChange={(draft) => setDoc((d) => d && { ...d, draft })}
              onDropSensor={(ref, label, x, y) => insert(sensorItem(ref, label, x, y))}
              itemName={(item) => describeItem(item, catalog)}
            />

            <div className={editorStyles.panels}>
              <section className={editorStyles.panel} aria-label="Sensors">
                <h3 className={editorStyles.panelTitle}>Sensors</h3>
                <SensorTree
                  catalog={catalog}
                  onAdd={(ref, label) => insert(sensorItem(ref, label, doc.draft.width / 2, doc.draft.height / 2))}
                />
              </section>
              <section className={editorStyles.panel} aria-label="Layout items">
                <h3 className={editorStyles.panelTitle}>Items</h3>
                <ItemList
                  items={doc.draft.items}
                  catalog={catalog}
                  selectedIds={selectedIds}
                  onSelect={setSelectedIds}
                  onRemove={(id) => {
                    setSelectedIds((ids) => ids.filter((i) => i !== id));
                    setDoc((d) => d && { ...d, draft: removeItem(d.draft, id) });
                  }}
                />
                <h3 className={editorStyles.panelTitle} style={{ marginTop: 'var(--space-4)' }}>
                  Background
                </h3>
                <BackgroundPicker
                  key={openCount}
                  background={doc.draft.background}
                  data={editorData}
                  onChange={(background) => {
                    // Only for the layout the picker was opened with (a late import must not touch another one).
                    const forOpen = openCount;
                    if (forOpen === openCountRef.current) setDoc((d) => d && { ...d, draft: { ...d.draft, background } });
                  }}
                />
              </section>
              <section className={editorStyles.panel} aria-label="Properties">
                <h3 className={editorStyles.panelTitle}>Properties</h3>
                {selected ? (
                  <Properties
                    key={selected.id}
                    item={selected}
                    data={editorData}
                    imageImport={imageImport}
                    onChange={(patch) => setDoc((d) => d && { ...d, draft: updateItem(d.draft, selected.id, patch) })}
                  />
                ) : selectedIds.length > 1 ? (
                  <p className={editorStyles.muted}>{selectedIds.length} items selected</p>
                ) : (
                  <p className={editorStyles.muted}>Select an item to change it.</p>
                )}
              </section>
            </div>
          </>
        )}
      </Card>

      {pending && doc && (
        <Dialog open title="Unsaved changes" onClose={() => !busy && setPending(null)}>
          <p>{doc.draft.name} has unsaved changes.</p>
          {pending.errors.length > 0 && (
            <div className={styles.errors} role="alert" style={{ marginTop: 'var(--space-3)' }}>
              <strong>Not saved:</strong>
              <ul>
                {pending.errors.map((err, i) => (
                  <li key={i}>{err}</li>
                ))}
              </ul>
            </div>
          )}
          <div className={styles.dialogActions}>
            <Button type="button" variant="secondary" disabled={busy} onClick={() => setPending(null)}>
              Cancel
            </Button>
            <Button type="button" variant="danger" disabled={busy} onClick={discardThenContinue}>
              Discard
            </Button>
            <Button type="button" disabled={busy} onClick={() => void saveThenContinue()}>
              Save
            </Button>
          </div>
        </Dialog>
      )}

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
            {dialog.kind === 'delete' && doc.savedName !== null && (
              <ScreensShowing devices={devices} layout={doc.savedName} />
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

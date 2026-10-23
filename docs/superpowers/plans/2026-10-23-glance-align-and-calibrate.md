# Editor Alignment Tools and Screen Colour Calibration Implementation Plan (Plan 2 addendum)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users line items up precisely in the layout editor (multi-select, snapping with guides, align and distribute) and calibrate each USB screen's colours so dark and translucent colours look on the panel as they do in the preview.

**Architecture:** Alignment is pure front-end: a new pure module `web/src/editor/align.ts` computes snap offsets and align/distribute moves from the layout-space rectangles the Canvas already has (`/api/render/bounds`); selection becomes a list of ids. Calibration is per device in `settings.json` (`gamma`, `brightness`), turned into a 256-entry lookup table applied in place to the device-orientation pixels just before JPEG encoding on every device path; the preview endpoint and `/api/render` stay uncalibrated. A temporary grey-step test pattern can be shown on a device while calibrating.

**Tech Stack:** As Plan 2: React 18 + TypeScript strict + Vite, Vitest, Playwright (msedge); .NET 8 + SkiaSharp, xUnit.

**Spec / origin:** Owner hardware smoke 2026-10-23 (`docs/testing/hardware-smoke.md` §Web UI results): the editor preview and the screen draw items at the same positions (measured within ~1 %), but hand-placed items are staggered (x = 31..38 px, rows 31–35 px apart for 31 px text) and the LCD shows dark/translucent colours much brighter and bluer than a monitor. Owner chose "alignment tools" and "screen colour calibration" and skipped further questions; the design calls below are the controller's (marked in the ledger). Base spec: `docs/superpowers/specs/2026-10-21-glance-design.md` §6.1, §8.

## Design decisions (controller's calls)

- **Selection:** `selectedIds: string[]`. Click selects one; Shift+click or Ctrl/Cmd+click toggles an item in or out (canvas boxes and the Items list); Ctrl/Cmd+A on the focused canvas selects all; Escape clears. Properties panel edits only when exactly one item is selected; with 2+ it shows "N items selected" and the align bar.
- **Group actions:** dragging any selected box moves all selected items by the same delta; Arrows / Shift+Arrows move all; Delete removes all; Ctrl+D duplicates all and selects the copies.
- **Snapping while dragging:** the dragged selection's union rectangle snaps its left/centre/right edges to other (unselected) items' left/centre/right and to canvas left/centre/right; same for top/middle/bottom. Threshold 6 screen px converted to layout px. Nearest candidate per axis wins. Matching lines are drawn as 1 px accent guides across the canvas while dragging. Holding Alt disables snapping. Arrow-key moves never snap.
- **Align / distribute:** align bar (shown with 2+ selected) with buttons Align left, Align centre, Align right, Align top, Align middle, Align bottom (relative to the selection's union rectangle), and Distribute horizontally / Distribute vertically (enabled with 3+; first and last stay put, equal gaps between rectangles, ordered by position). All use the drawn rectangles from `/api/render/bounds` (text = ink bounds), falling back to the hit-box rect when a bound is missing. Resulting positions round to whole pixels.
- **Calibration values:** `gamma` 0.5–3.0, default 1.0, step 0.05; `brightness` 10–100 (%), default 100. Output per channel: `round(255 · brightness/100 · (v/255)^gamma)`, clamped 0–255. gamma > 1 darkens dark tones (what this LCD needs). Defaults are identity and skip the pass entirely (no cost for users who never calibrate).
- **Where it applies:** only to bytes sent to the device (still, fallback, animated and passthrough paths). `/api/preview/{key}.jpg`, `/api/render` and the editor stay uncalibrated so they match a normal monitor. Passthrough frames are cached per calibration (calibration joins the cache key; non-identity calibration disables the raw-JPEG shortcut).
- **Test pattern:** `POST /api/devices/{key}/test-pattern` shows a full-screen pattern on that device for 120 s (or until `DELETE`): 16 grey steps from 0 to 255 across the top half, and the bottom half split into black, `#101820` (the default layout background), `#33FFFFFF` over `#101820` (the default track colour), and white, each labelled. It is drawn in layout orientation and goes through the normal rotation + calibration path. Not persisted.

## Global Constraints

- Branch `feat/web-ui`, worktree `D:\WS\Own\glance-web`. Commit at the end of every task (small, meaningful, conventional message ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). No push, no PR.
- All Plan 2 Global Constraints still hold (no runtime deps beyond react/react-dom, no innerHTML/eval, all API calls via `web/src/api/client.ts`, types mirror `docs/api.md`, 360 px usable, keyboard reachable with visible focus).
- Nothing from vendor software in the repo.
- `docs/api.md` updated in the same task as any API change; `web/src/api/types.ts` mirrors it.
- Test commands: `dotnet test Glance.sln --filter "Category!=Hardware"`; in `web/`: `npm test`, `npm run build`, `npm run e2e`.

## Review Focus

1. **Calibration must never reach the preview or the editor render** → `/api/preview` and `/api/render` bytes are identical with and without calibration. Test in Task 4.
2. **Calibration changes apply live without reconnecting the device, and passthrough animations don't keep serving frames cached with the old calibration** → frame bytes change after a PUT; cached passthrough frames are rebuilt. Test in Task 4.
3. **Invalid calibration values (NaN, 0, 5, -1, strings, hand-edited settings.json)** → 400 from the API with a clear error; `AppSettings.Validate` rejects bad stored values. Test in Task 4.
4. **Snapping must not fight the user** → Alt disables it; it never pulls more than the threshold; a drag ending off any guide leaves the item exactly where the pointer put it. Test in Task 2.
5. **Group operations keep the draft consistent** → delete/duplicate/move of a multi-selection is one draft change (one undoable dirty state, one render), and selection never references removed ids. Test in Task 1.

---

## File Structure

```
web/src/editor/model.ts          moveItems, removeItems, duplicateItems (Task 1)
web/src/editor/align.ts          snapMove, alignMoves, distributeMoves, unionRect (Tasks 2-3)
web/src/editor/Canvas.tsx        multi-select, group drag/keys, snapping + guides, align bar host (Tasks 1-3)
web/src/editor/AlignBar.tsx      align / distribute buttons (Task 3)
web/src/editor/ItemList.tsx      selectedIds + modifier-click (Task 1)
web/src/tabs/LayoutsTab.tsx      selectedIds state, Properties only for one item (Task 1)
src/Glance.Core/Layouts/AppSettings.cs        DeviceSettings.Gamma/Brightness + validation (Task 4)
src/Glance.Core/Rendering/ColorCalibration.cs LUT build + in-place apply (Task 4)
src/Glance.Core/Rendering/TestPattern.cs      pattern bitmap (Task 4)
src/Glance.Core/Sessions/DisplaySession.cs    apply calibration on device paths, test-pattern plan (Task 4)
src/Glance.Core/Rendering/FrameCompositor.cs  optional calibration before encode (Task 4)
src/Glance.Service/Api/DevicesApi.cs          gamma/brightness in view + PUT, test-pattern endpoints (Task 4)
web/src/devices/Calibration.tsx               sliders, reset, test pattern toggle (Task 5)
web/src/tabs/DevicesTab.tsx                   hosts Calibration per connected device (Task 5)
```

---

### Task 1: Multi-selection and group actions in the editor

**Files:**
- Modify: `web/src/editor/model.ts`, `web/src/editor/Canvas.tsx`, `web/src/editor/ItemList.tsx`, `web/src/tabs/LayoutsTab.tsx`
- Test: `web/src/editor/model.test.ts`, `web/e2e/layouts-select.spec.ts` (new)

**Interfaces:**
- Produces (model.ts):
  - `moveItems(layout: Layout, moves: Record<string, { dx: number; dy: number }>): Layout` — adds rounded deltas to x/y of each listed id; unknown ids ignored; returns the same object when nothing changes.
  - `removeItems(layout: Layout, ids: readonly string[]): Layout`
  - `duplicateItems(layout: Layout, ids: readonly string[]): { layout: Layout; newIds: string[] }` — same offset/id rules as `duplicateItem`, copies appended in the original order.
- Produces (Canvas props): `selectedIds: readonly string[]; onSelect: (ids: string[]) => void` replace `selectedId`/`onSelect(id|null)`. ItemList gets the same two props.

- [ ] **Step 1: Write failing unit tests** in `model.test.ts`: `moveItems` moves two of three items by (5,-3) and leaves the third; rounds 1.6 → 2; returns the input object for `{}`. `removeItems` removes two ids in one call. `duplicateItems` returns copies with fresh unique ids in original order and `newIds` matching them.
- [ ] **Step 2: Write failing e2e tests** in `layouts-select.spec.ts` (mocked API, "Default" layout, role-based queries; canvas boxes by `getByRole('button', { name: … })` inside the canvas):
  - Shift+click two boxes → both `aria-pressed="true"`; Properties shows "2 items selected"; Ctrl+click one again → only one pressed and Properties shows its fields.
  - Items list: Ctrl+click adds; plain click selects only that one.
  - Drag one of two selected boxes by +40 px layout → both items' x increase by 40 in the saved PUT body.
  - ArrowRight with two selected → both x +1; Delete → both removed and no box pressed; Ctrl+D with two selected → 2 new items, the copies are the pressed ones.
  - Ctrl+A on focused canvas selects all; Escape clears.
  - Group delete marks the draft dirty once and the render endpoint is called once after the debounce (Review Focus 5).
- [ ] **Step 3: Run** `npm test -- model` and `npx playwright test e2e/layouts-select.spec.ts` → FAIL.
- [ ] **Step 4: Implement.** LayoutsTab holds `selectedIds` (prune ids no longer in the draft whenever the draft changes). Canvas: pointerdown on a box with Shift/Ctrl/Meta toggles membership and does not start a drag when it removed the item; otherwise, if the box is not selected, select only it; the drag stores origins of all selected items and applies one `moveItems` per pointermove. Keys act on all selected. Update the canvas hint text: add "Shift+click adds to the selection".
- [ ] **Step 5: Run** unit + the new spec + full `npm run e2e` → PASS (existing single-selection tests keep passing).
- [ ] **Step 6: Commit** `feat(ui): select several layout items and move, delete or duplicate them together`.

### Task 2: Snapping with guide lines while dragging

**Files:**
- Create: `web/src/editor/align.ts`, `web/src/editor/align.test.ts`
- Modify: `web/src/editor/Canvas.tsx`, `web/src/editor/Editor.module.css`
- Test: `web/e2e/layouts-snap.spec.ts` (new)

**Interfaces:**
- Consumes: Task 1 group drag; `Rect` from `Canvas.tsx` (move the `Rect` type into `align.ts` and re-export from Canvas).
- Produces (align.ts):
  - `unionRect(rects: readonly Rect[]): Rect`
  - `type Guide = { axis: 'x' | 'y'; at: number }` (layout px; `axis: 'x'` is a vertical line at x = at)
  - `snapMove(moving: Rect, targets: readonly Rect[], canvas: { width: number; height: number }, dx: number, dy: number, threshold: number): { dx: number; dy: number; guides: Guide[] }` — candidates per axis: moving edges {start, centre, end} after (dx,dy) against target edges {start, centre, end} and canvas {0, size/2, size}; the smallest |difference| ≤ threshold adjusts that axis; guides list every target line hit by the adjusted rect (deduplicated).

- [ ] **Step 1: Write failing unit tests** in `align.test.ts` (canvas 1280×480, threshold 6), moving rect `{x:10, y:10, width:100, height:20}`, target `{x:200, y:300, width:50, height:50}`:
  - dx 187, dy 0 (left lands at 197, 3 px from the target's left) → `dx: 190`, guides contain `{axis:'x', at: 200}`.
  - centre within threshold of canvas centre (640) → snapped; guide at 640.
  - 7 px away with threshold 6 → dx unchanged, no guides.
  - both axes snap independently; nearest of two candidates wins.
- [ ] **Step 2: Write failing e2e test** in `layouts-snap.spec.ts`: drag "Bar — CPU" so its left lands ~3 layout px from the left of the "CPU temperature" text's drawn bounds (mock `/api/render/bounds` with fixed rects) → saved x equals the aligned value and a guide element (`data-testid="snap-guide"`, decorative, `aria-hidden`) is visible during the drag and gone after pointerup; same drag with Alt held → x is exactly the unsnapped value; drag ending 20 px away → no snap (Review Focus 4).
- [ ] **Step 3: Run** → FAIL.
- [ ] **Step 4: Implement.** Threshold = 6 screen px × (layout width / stage width). Targets = drawn rects (bounds, else hitBox) of unselected items. Apply `snapMove` to the selection's union rect at drag start offset by the raw delta; skip when `e.altKey`. Render guides as absolutely positioned 1 px lines (`var(--accent)`) inside the stage while a drag is active.
- [ ] **Step 5: Run** `npm test`, the new spec, full e2e → PASS.
- [ ] **Step 6: Commit** `feat(ui): snap dragged items to other items and the canvas with guide lines`.

### Task 3: Align and distribute

**Files:**
- Create: `web/src/editor/AlignBar.tsx`
- Modify: `web/src/editor/align.ts`, `web/src/editor/Canvas.tsx`
- Test: `web/src/editor/align.test.ts`, `web/e2e/layouts-align.spec.ts` (new)

**Interfaces:**
- Consumes: Task 1 `moveItems`, `selectedIds`; Task 2 `Rect`, `unionRect`.
- Produces (align.ts):
  - `type AlignMode = 'left' | 'centre' | 'right' | 'top' | 'middle' | 'bottom'`
  - `alignMoves(rects: Record<string, Rect>, mode: AlignMode): Record<string, { dx: number; dy: number }>`
  - `distributeMoves(rects: Record<string, Rect>, axis: 'x' | 'y'): Record<string, { dx: number; dy: number }>` — sort by start on that axis; first and last fixed; gap = (last.end − first.start − Σ sizes) / (n − 1); may be negative (overlap) — still equal.
- AlignBar: `{ count: number; onAlign: (m: AlignMode) => void; onDistribute: (a: 'x' | 'y') => void }`; a `role="toolbar"` with `aria-label="Align"`, eight buttons with the exact names from the design decisions, distribute buttons `disabled` when count < 3. Shown above the canvas when count ≥ 2.

- [ ] **Step 1: Write failing unit tests**: three rects with lefts 31, 33, 36 → 'left' gives dx 0, −2, −5; 'centre' on widths 100/200 aligns centres to the union centre; 'bottom' aligns bottoms; distribute 'y' of rows at y 51, 86, 154 with heights 31,24,31 → middle row moved so both gaps equal; two rects → `distributeMoves` returns zero moves.
- [ ] **Step 2: Write failing e2e test** `layouts-align.spec.ts`: select the three text items (Shift+click), click "Align left" → their saved x values make the drawn lefts equal (mocked bounds); "Distribute vertically" → equal gaps; with two selected the distribute buttons are disabled; toolbar reachable by Tab and buttons have accessible names.
- [ ] **Step 3: Run** → FAIL.
- [ ] **Step 4: Implement** in Canvas: rects for selected ids from bounds (fallback hitBox), one `moveItems` per click.
- [ ] **Step 5: Run** all web suites → PASS.
- [ ] **Step 6: Commit** `feat(ui): align and distribute selected layout items`.

### Task 4: Per-device colour calibration and test pattern (service)

**Files:**
- Create: `src/Glance.Core/Rendering/ColorCalibration.cs`, `src/Glance.Core/Rendering/TestPattern.cs`
- Modify: `src/Glance.Core/Layouts/AppSettings.cs`, `src/Glance.Core/Rendering/FrameCompositor.cs`, `src/Glance.Core/Sessions/DisplaySession.cs`, `src/Glance.Service/Api/DevicesApi.cs`, `docs/api.md`, `web/src/api/types.ts` (DeviceView fields only)
- Test: `tests/Glance.Core.Tests/Rendering/ColorCalibrationTests.cs` (new), `tests/Glance.Core.Tests/Sessions/DisplaySessionTests.cs`, `tests/Glance.Service.Tests/ApiTests.cs`

**Interfaces:**
- Produces (Core):
  - `DeviceSettings.Gamma` (`double`, default 1.0), `DeviceSettings.Brightness` (`int`, default 100); `AppSettings.Validate` adds `device '{key}': gamma must be 0.5..3` / `brightness must be 10..100` (NaN invalid).
  - `public sealed class ColorCalibration { public static ColorCalibration? For(double gamma, int brightness); public void Apply(Span<byte> bgra); }` — `For` returns null for the identity (gamma 1, brightness 100); `Apply` maps B, G and R bytes through one 256-entry table and leaves alpha.
  - `FrameCompositor` constructor gains optional `ColorCalibration? calibration = null`, applied to the device bitmap in `Encode()` before encoding.
  - `TestPattern.Draw(SKCanvas canvas, int width, int height)` — the pattern from the design decisions, in layout orientation.
  - `DisplaySession.ShowTestPattern(TimeSpan duration)` / `StopTestPattern()` — in-memory; invalidates the plan; while active the session sends the pattern (still path) instead of the layout and the preview shows it too.
- Produces (API):
  - `DeviceView` gains `double Gamma, int Brightness`; `DevicePatch` gains `double? Gamma, int? Brightness`. Errors: `400 {"error":"gamma must be 0.5..3"}`, `400 {"error":"brightness must be 10..100"}`.
  - `POST /api/devices/{key}/test-pattern` (no body) → `204`, 120 s; `DELETE /api/devices/{key}/test-pattern` → `204`; `404 {"error":"unknown device"}` for an unknown or disconnected key. Both hold nothing else; documented in `docs/api.md`.

- [ ] **Step 1: Write failing tests**
  - `ColorCalibrationTests`: `For(1, 100)` is null; `For(2.2, 100)` maps 0→0, 255→255, 128→56 (round(255·(128/255)^2.2)); `For(1, 50)` maps 255→128; alpha byte untouched.
  - `DisplaySessionTests` (fake connection, existing helpers): with gamma 2.2 a still layout's sent frame decoded has a darker mid-grey pixel than with defaults; `GetPreviewJpeg()` bytes are the same with and without calibration (Review Focus 1); changing gamma via `SettingsStore.Update` makes the next sent frame differ without reconnect (Review Focus 2); a passthrough animation sends different bytes after a calibration change (cache rebuilt); `ShowTestPattern(…)` sends a frame whose top-left step is black and top-right step white (after undoing rotation 0), and after the duration the layout frame returns (use the fake TimeProvider).
  - `ApiTests`: PUT gamma 2.2 / brightness 80 round-trips in GET; PUT gamma 0, 3.5, NaN-as-string, brightness 5 and 101 → 400 with the exact messages (Review Focus 3); POST/DELETE test-pattern → 204, unknown key → 404; `/api/render` bytes unaffected by a device's calibration.
  - `AppSettings.Validate` rejects gamma 0.4 and brightness 0 in a hand-edited settings object.
- [ ] **Step 2: Run** `dotnet test Glance.sln --filter "Category!=Hardware"` → new tests FAIL.
- [ ] **Step 3: Implement.** Session reads calibration from the device settings when building a plan (settings changes already invalidate the plan). Apply in: `EncodeForDevice` (on the rotated copy, or on a copy when rotation is 0 — never on the preview bitmap), `FrameCompositor.Encode()`, passthrough (`Passthrough` record gains the calibration key `(Gamma, Brightness)`; non-identity disables the raw-JPEG shortcut in `PrepareFrame`). Preview paths unchanged.
- [ ] **Step 4: Run** all .NET tests → PASS; update `docs/api.md` and `web/src/api/types.ts`; `npm run build` → PASS.
- [ ] **Step 5: Commit** in two commits: `feat(core): per-device colour calibration on frames sent to the screen` and `feat(service): calibration fields and test pattern endpoints for devices`.

### Task 5: Calibration controls in the Devices tab

**Files:**
- Create: `web/src/devices/Calibration.tsx`
- Modify: `web/src/devices/DeviceControls.tsx` (`DevicePatch` type gains `gamma?`, `brightness?`), `web/src/api/client.ts` (test-pattern calls), `web/src/tabs/DevicesTab.tsx`
- Test: `web/src/devices/Calibration.test.tsx`, `web/e2e/devices.spec.ts`, `web/e2e/mockApi.ts`

**Interfaces:**
- Consumes: Task 4 API; `useDeviceUpdate(device, onUpdated)`.
- Produces: `Calibration: React.FC<{ device: DeviceView; onUpdated: (d: DeviceView) => void }>` — a "Screen colour" fieldset with:
  - "Gamma" range 0.5–3.0 step 0.05 with its value shown (2 decimals) and "Brightness" range 10–100 step 1 with "%"; native `<input type="range">` with labels.
  - Changes PUT after 150 ms of no change, latest value wins (out-of-order answers ignored); a failed PUT shows the server error under the fieldset and restores the last saved values.
  - "Reset" button → gamma 1, brightness 100.
  - "Show test pattern" toggle button (`aria-pressed`) → POST; pressing again → DELETE; the component DELETEs on unmount if it is on; the toggle turns itself off after 120 s.
  - A hint: "Raise gamma until the dark grey steps on the screen look like they do here." next to a small inline rendering of the same 16 steps (CSS gradient-free: 16 divs).
  - Only for devices with status `connected`.

- [ ] **Step 1: Write failing tests**: component test — moving the gamma slider three times quickly sends one PUT with the last value; Reset sends `{gamma:1, brightness:100}`; a 400 shows the message and restores values. e2e — on Devices tab the fieldset is visible for the connected device and not for a disconnected one; toggling the test pattern POSTs then DELETEs; navigating away while on sends DELETE; controls reachable by keyboard and usable at 360 px.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement**; extend `mockApi.ts` with the new fields and endpoints.
- [ ] **Step 4: Run** `npm test`, `npm run build`, `npm run e2e` → PASS.
- [ ] **Step 5: Commit** `feat(ui): calibrate a screen's colours from the Devices tab`.

### Task 6: Owner re-smoke (controller with owner)

- [ ] Publish to `artifacts/run`, restart elevated (UAC), owner: re-arrange "Plain" with Shift+click + Align left + Distribute vertically, check the screen; open the test pattern and tune gamma/brightness until the dark steps match the monitor; confirm the focus ring fix on the tab bar.
- [ ] Record results (and the earlier smoke answers) in `docs/testing/hardware-smoke.md` §Web UI results; commit `docs: record the web UI hardware smoke`.

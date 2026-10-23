# Glance Web UI Implementation Plan (Plan 2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The browser UI from spec §8 — tabs Display, Layouts (drag-sensors-from-list editor), Sensors, Devices, Settings, plus login — served by the existing service, with the few API additions it needs.

**Architecture:** A React + TypeScript single-page app in `web/`, built with Vite into `src/Glance.Service/wwwroot` (build output, git-ignored) so the service serves static files and needs no Node.js at runtime. `dotnet publish` of the service runs the web build. During development `npm run dev` serves the UI with a proxy to the service. Pure logic (API client, editor model, schema, scheduler, formatting) is unit-tested with Vitest; components with Testing Library; whole pages end-to-end with Playwright Test driving the installed Edge against `vite preview` with mocked `/api/*` routes. The editor canvas shows the server-rendered draft (`POST /api/render`) with selection boxes positioned from `POST /api/render/bounds`, so preview and device never drift.

**Tech Stack:** React 18, TypeScript (strict), Vite, CSS Modules + CSS custom properties, EventSource (SSE), native HTML5 drag-and-drop + pointer events; Vitest + @testing-library/react + jsdom; @playwright/test (channel `msedge`); .NET 8 for API additions (xUnit). Owner decision 2026-10-22: React + Vite + TypeScript instead of plain HTML/JS.

**Spec:** `docs/superpowers/specs/2026-10-21-glance-design.md` (§1, §6.1, §7.1, §8, §9.1). API contract: `docs/api.md` (Task 1).

## Global Constraints

- Branch `feat/web-ui`. **Commit at the end of every task** (small, meaningful, conventional message ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). **No push, no PR** until the owner decides.
- Nothing from vendor software in the repo (no vendor names in UI copy except "Thermalright" as supported-hardware fact; no vendor images).
- **No Node.js at runtime and no CDN/remote assets** (spec §8; LAN/offline use): everything the browser loads is bundled by Vite into `wwwroot`. Node.js (≥ 20) is a build-time requirement only.
- Dependencies: runtime only `react` and `react-dom`; everything else is a devDependency. No UI component library, no router library, no state library, no CSS framework — hand-written, small. Every npm package (runtime and dev) must be MIT/Apache-2.0/ISC/BSD and recorded in THIRD-PARTY-NOTICES.md. `package-lock.json` committed.
- **No `dangerouslySetInnerHTML`, `innerHTML`, `eval`, `new Function`** anywhere in `web/src` — a unit test scans the sources and fails on any use. All user/sensor strings render as React text.
- TypeScript `strict: true`; `npm run build` must pass `tsc --noEmit` with zero errors; ESLint not required.
- All API calls go through `web/src/api/client.ts`: same-origin, JSON bodies with `Content-Type: application/json`, uploads `multipart/form-data` (AccessGate rules from Plan 1). API types in `web/src/api/types.ts` mirror `docs/api.md`.
- Clean, calm UI (spec §1): one accent colour, generous spacing, system font stack, dark theme default, light theme via `prefers-color-scheme`; usable from 360 px wide (phones on LAN).
- One shared EventSource (`/api/events`) via a React context; preview images refreshed ≤ 1/s and only while `document.visibilityState === "visible"`.
- Test commands: `dotnet test Glance.sln --filter "Category!=Hardware"`; in `web/`: `npm test` (Vitest, run mode), `npm run build`, `npm run e2e` (Playwright; skips with a clear message if Microsoft Edge is not installed).

## Review Focus

1. **Remote session expires mid-edit (401)** → user is sent to login and, after logging in, lands back on the Layouts tab with the unsaved draft restored (draft kept in `sessionStorage`). Test in Task 2 (redirect) and Task 6 (draft restore).
2. **Service restarts / network drops** → a visible "Reconnecting…" banner appears, live values grey out instead of showing stale numbers, and everything resumes when SSE reconnects. Test in Task 2.
3. **Unsaved edits when switching layout, tab, or closing the page** → confirm prompt (`beforeunload` + in-app dialog). Test in Task 6.
4. **Server rejects a layout (400 `{errors}`)** → errors shown next to Save, editor stays usable, nothing lost. Test in Task 5.
5. **Rapid edits / dragging** → draft renders debounced (150 ms), at most one request in flight, stale responses discarded. Test in Task 4 (scheduler) and Task 5.

---

## File Structure

```
src/Glance.Service/
  Api/UiSupportApi.cs, Api/DevicesApi.cs, Api/EventsApi.cs   (Task 1)
  Glance.Service.csproj (modify)   web build on publish         (Task 2)
  wwwroot/                         Vite build output (git-ignored; placeholder removed)
docs/api.md                        API contract                  (Task 1)
web/
  package.json, package-lock.json, tsconfig.json, vite.config.ts, playwright.config.ts, index.html
  src/main.tsx, src/App.tsx         shell, hash router, tab bar, connection banner
  src/api/client.ts, types.ts       fetch wrapper, ApiError, 401 handling, API types
  src/live/LiveProvider.tsx         shared EventSource context + hooks (useSensors, useDevices, useLiveStatus)
  src/lib/format.ts                 value formatting
  src/login/LoginPage.tsx
  src/tabs/DisplayTab.tsx, SensorsTab.tsx, DevicesTab.tsx, SettingsTab.tsx, LayoutsTab.tsx
  src/editor/model.ts, schema.ts, scheduler.ts      pure logic
  src/editor/Canvas.tsx, SensorTree.tsx, Properties.tsx, BackgroundPicker.tsx, AddMenu.tsx
  src/ui/*.tsx, *.module.css, tokens.css           small shared components (Button, Card, Field, Dialog, Banner, Select)
  src/**/*.test.ts(x)               Vitest unit/component tests
  e2e/*.spec.ts, e2e/mockApi.ts     Playwright tests + route mocks
```

---

### Task 1: API additions for the UI + API contract doc

**Files:**
- Modify: `src/Glance.Core/Rendering/OverlayRenderer.cs`, `src/Glance.Service/Api/DevicesApi.cs`, `src/Glance.Service/Api/EventsApi.cs`, `src/Glance.Service/Program.cs`
- Create: `src/Glance.Service/Api/UiSupportApi.cs`, `docs/api.md`
- Test: `tests/Glance.Core.Tests/Rendering/MeasureItemsTests.cs`, `tests/Glance.Service.Tests/UiSupportApiTests.cs`, additions in `ApiTests.cs`

**Interfaces:**
- Produces:
  - `OverlayRenderer.MeasureItems(Layout layout, RenderContext ctx) → IReadOnlyDictionary<string, SKRect>` — layout-space bounds per item id, using the same measurement code paths as drawing (text: font metrics of the exact string drawn; gauges/images: x,y,width,height).
  - `POST /api/render/bounds` body = Layout JSON → `{ "<itemId>": { "x":, "y":, "width":, "height": } }` (same validation as `/api/render`; holds the UI-interest lease).
  - `GET /api/fonts` → `{ "default": "Inter", "families": ["Inter", ...system families sorted, distinct] }`.
  - `GET /api/timezones` → `[{ "id": "Asia/Tokyo", "label": "(UTC+09:00) Tokyo" }, ...]` IANA ids (convert Windows ids with `TimeZoneInfo.TryConvertWindowsIdToIanaId`), sorted by offset then label, plus `{ "id": "local", "label": "Local time" }` first.
  - `GET /api/devices` adds `fps` (double, `DisplaySession.Fps`, 0 when no session) and `framesSent`.
  - `PUT /api/devices/{key}` body `{ "activeLayout": null }` **explicitly present** clears the active layout (field absent = unchanged; use `JsonElement` presence as in SettingsApi).
  - SSE `/api/events` also emits `event: devices` whenever `SettingsStore.Changed` fires (rotation/layout changes), not only on `DevicesChanged`.

- [ ] **Step 1: Write failing tests** — `MeasureItems_text_bounds_match_drawn_pixels` (render a sensor item, assert all non-transparent pixels lie inside the measured rect inflated by 1 px, and the rect is no more than 4 px larger than the pixel bounds on each side); `MeasureItems_gauge_uses_declared_size`; API tests: `Render_bounds_returns_rect_per_item`, `Fonts_include_Inter_default`, `Timezones_start_with_local_and_contain_Asia_Tokyo`, `Devices_report_fps`, `Put_device_with_null_activeLayout_clears_it_but_absent_keeps_it`, `Settings_change_emits_devices_event` (open SSE, PUT rotation, read until `event: devices` within 5 s).
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~MeasureItems|FullyQualifiedName~UiSupportApi|FullyQualifiedName~ApiTests"` — Expected: FAIL.
- [ ] **Step 3: Implement.** Refactor item renderers so measuring and drawing share one function per item type (no duplicated layout maths).
- [ ] **Step 4: Write `docs/api.md`** — every route (method, path, body, response, status codes, auth rule), the SSE event formats, the error shapes (`{error}` / `{errors:[]}`), content-type and Origin rules. Source of truth for the UI tasks.
- [ ] **Step 5: Run** full `dotnet test --filter "Category!=Hardware"` — Expected: PASS, 0 warnings.
- [ ] **Step 6: Commit** `feat(api): add render bounds, fonts, timezones and device fps for the web UI`

---


### Task 2: Web project, build integration, app shell, API client, live events, login

**Files:**
- Create: `web/` project (package.json scripts `dev`, `build` = `tsc --noEmit && vite build`, `preview`, `test` = `vitest run`, `e2e` = `playwright test`), `vite.config.ts` (outDir `../src/Glance.Service/wwwroot`, `emptyOutDir: true`; dev proxy `/api` → `http://localhost:8735` with the `Origin` header removed and `changeOrigin: true` so the service's Host/Origin gate accepts it; SSE must stream through the proxy), `index.html`, `src/main.tsx`, `src/App.tsx`, `src/api/{client,types}.ts`, `src/live/LiveProvider.tsx`, `src/lib/format.ts`, `src/login/LoginPage.tsx`, `src/ui/*` (Button, Card, Field, Dialog, Banner, Select, tokens.css), tab stubs, `e2e/mockApi.ts`, `e2e/shell.spec.ts`, `src/security.test.ts`
- Modify: `src/Glance.Service/Glance.Service.csproj` (MSBuild target before publish: `npm ci` + `npm run build` in `web/`; clear error if `node`/`npm` missing; skip in plain `dotnet build`/`dotnet test`), `.gitignore` (`src/Glance.Service/wwwroot/`, `web/node_modules/`, `web/test-results/`, `web/playwright-report/`), `THIRD-PARTY-NOTICES.md`
- Delete: `src/Glance.Service/wwwroot/index.html` (placeholder) — service tests that requested `/` must be updated to not depend on it (or a test fixture serves a stub).

**Interfaces:**
- `client.ts`: `class ApiError extends Error { status: number; errors: string[] }`; `api<T>(method, path, body?): Promise<T>` (throws `ApiError`; 401 → `onUnauthorized(location.hash)` → navigates to `#login?return=<encoded hash>`); `upload<T>(path, form: FormData): Promise<T>`; `setUnauthorizedHandler(fn)`.
- Routing (hash, hand-written `useHashRoute()`): `#display` (default), `#layouts`, `#layouts/<name>`, `#sensors`, `#devices`, `#settings`, `#login?return=…`. Tab components: `({ navigate }: { navigate(hash: string): void }) => JSX`.
- `LiveProvider`: one EventSource; `useLiveStatus(): 'connecting'|'open'|'reconnecting'`; `useSensorValues(): Record<string, number|null>`; `useDevices(): DeviceView[] | null`; reconnect backoff 1 → 2 → 5 s; header banner "Reconnecting…" when not open; live values rendered greyed (`aria-busy`) while reconnecting.
- `format.ts`: `formatValue(value: number|null|undefined, unit?: string, decimals = 0): string` (null/non-finite → "—").
- `e2e/mockApi.ts`: `mockApi(page, seed?)` installs `page.route('**/api/**')` handlers backed by an in-memory store (one connected 1280×480 device `thermalright-hid-5302_SER1`, sensors `cpu/t` and `/amdcpu/0/temperature/2` plus a GPU group, roles, two layouts, one asset), records requests (`calls`), and serves `/api/events` as an SSE response the test can push to / close. Later tasks extend it (Ruling: each task adds the routes its tests need).

- [ ] **Step 1: Failing tests** — Vitest: `api throws ApiError with errors[] on 400`, `api invokes unauthorized handler with current hash on 401`, `api sends JSON content type`, `formatValue null → "—"`, `formatValue rounds`, `no dangerouslySetInnerHTML/innerHTML/eval/new Function in src` (scans files). Playwright: `tabs switch by hash and highlight active`, `401 redirects to login with return`, `login posts password and returns to hash`, `reconnecting banner shows when events drop and hides after reconnect`.
- [ ] **Step 2: Run** `npm test` and `npm run e2e` — Expected: FAIL.
- [ ] **Step 3: Implement** per Interfaces; design tokens in `tokens.css` (`--bg --surface --text --muted --accent --danger`, spacing 4/8/12/16/24/32, radius 10px).
- [ ] **Step 4: Run** `npm test`, `npm run build`, `npm run e2e`, `dotnet test Glance.sln --filter "Category!=Hardware"`, and `dotnet publish src/Glance.Service -c Release -o <temp dir>` (assert `wwwroot/index.html` and hashed assets exist in the output) — Expected: all PASS.
- [ ] **Step 5: Commit** (may be 2–3 commits: scaffold+build integration; shell+client+live; login) e.g. `build(web): add React + Vite + TypeScript project built into wwwroot`.

### Task 3: Display tab
**Files:** `web/src/tabs/DisplayTab.tsx` (+ module CSS), `e2e/display.spec.ts`, component tests as useful.
**Behaviour (spec §8.1):** card per device: live preview `<img>` (`/api/preview/{key}.jpg?t=<n>`, refreshed every 1 s while visible, keeps last image on 404), name, resolution, status, fps, active-layout select (PUT `activeLayout`), rotation buttons 0/90/180/270 with prominent "Flip 180°"; no devices → "No screen found — plug in a supported display"; no active layout → call to action "Choose a layout" / "Create a layout" (navigates `#layouts` and starts a new layout); retrying/unsupported shown with their message.
- [ ] Failing Playwright tests: `shows device card with preview and fps`, `changing layout PUTs activeLayout`, `Flip 180° PUTs rotation`, `preview refresh pauses while hidden`, `empty state without devices`, `first-run call to action without active layout` → run FAIL → implement → PASS → **commit** `feat(ui): add Display tab with live preview and quick controls`.

### Task 4: Editor model, property schema, render scheduler (pure TS)
**Files:** `web/src/editor/{model,schema,scheduler}.ts` + `.test.ts`
**Interfaces:**
- `model.ts`: types `Layout`, `Item` (discriminated union on `type` for all 10 types, fields per `docs/api.md`); `newLayout(name, width, height)` (colour background `#101820`); `defaultItem(type, x, y, extra?)` (unique 8-char id; defaults: font Inter 32/400, text `#FFFFFF`, gauge `#4AAFFF` / track `#33FFFFFF`, sizes round 200×200, bar 300×24, graph 400×150, sparkline 160×40, windowSeconds 60); `sensorItem(ref, label, x, y)`; `moveItem`, `removeItem`, `duplicateItem` (+16,+16, new id), `updateItem(layout, id, patch)`; `toLayoutPoint(clientX, clientY, rect: DOMRect, w, h)`; all immutable.
- `schema.ts`: `fieldsFor(type): Field[]`, `Field = { key; label; kind: 'text'|'number'|'color'|'select'|'sensor'|'font'|'timezone'|'thresholds'|'bool'|'asset'; min?; max?; step?; options? }`; numeric bounds copied from `src/Glance.Core/Layouts/LayoutLimits.cs` — test asserts the copy so drift is caught.
- `scheduler.ts`: `createRenderScheduler<T, R>(renderFn: (t: T, signal: AbortSignal) => Promise<R>, onResult: (r: R) => void, { delayMs = 150 })` → `{ request(t), dispose() }`: debounced; ≤ 1 in flight; latest wins; superseded results dropped; dispose aborts.
- [ ] Failing Vitest: defaults valid for every type vs bounds; `moveItem clamps and rounds`; `toLayoutPoint scales`; immutability; `fieldsFor covers every type`; `bounds match LayoutLimits`; scheduler `debounces`, `never two in flight`, `drops stale results` (fake timers) → FAIL → implement → PASS → **commit** `feat(ui): add layout editor model, property schema and render scheduler`.

### Task 5: Layouts tab — list, canvas, select/move, save, make active
**Files:** `web/src/tabs/LayoutsTab.tsx`, `web/src/editor/Canvas.tsx`, `e2e/layouts-canvas.spec.ts`
**Behaviour:** layout list (+ New, Duplicate, Rename, Delete with confirm); canvas = `<img>` of `POST /api/render` (scheduler on every edit + every 1 s for live values while visible) with absolutely positioned selection boxes from `POST /api/render/bounds`; click selects, pointer-drag moves, arrows 1 px / Shift 10 px, Delete removes, Ctrl/Cmd+D duplicates; canvas scales to width keeping aspect; toolbar Save (PUT; `{errors}` shown inline, edits kept), Make active (device choice when >1), unsaved marker; "Made for W×H" when the layout doesn't match the device canvas.
- [ ] Failing Playwright: `selecting layout renders preview`, `drag moves item and marks unsaved`, `arrow keys nudge`, `save PUTs and clears unsaved`, `save errors shown and edits kept`, `make active PUTs activeLayout`, `rapid drags keep ≤1 render in flight`, `size mismatch warning` → FAIL → implement → PASS → **commit** `feat(ui): add layout canvas with selection, dragging and saving`.

### Task 6: Layouts tab — sensor tree, add items, properties, background, unsaved guard
**Files:** `web/src/editor/{SensorTree,Properties,BackgroundPicker,AddMenu}.tsx`, modify `LayoutsTab.tsx`, `e2e/layouts-edit.spec.ts`
**Behaviour (spec §8.2):** sensor tree grouped by hardware kind with live values and aliases, search; drag a sensor onto the canvas → `sensor` item at the drop point (label = alias or name); "Roles" group drops `role:…`; Add menu (+ Text, + Clock, + Image, + Gauge ▸ Bar/Segmented bar/Arc gauge/Dial/History graph/Sparkline) adds at canvas centre and selects; properties panel generated from `schema.ts` (sensor = searchable select of sensors and roles; fonts from `/api/fonts`; time zones from `/api/timezones`; thresholds rows; asset picker with thumbnails); background picker Colour | Image | Animation with asset grid and "Import…" (kind by extension; `.zt` asks rotation 0/90/180/270, default 0, "rotation the file was made for"); unsaved guard (`beforeunload`; dialog Save/Discard/Cancel on layout/tab switch); draft stashed in `sessionStorage` per layout and restored after re-login.
- [ ] Failing Playwright: `dragging sensor adds bound item at drop point`, `dragging role adds role binding`, `sensor tree values update live`, `search filters tree`, `add menu adds each type`, `property change rerenders`, `thresholds editor add/remove`, `zt import asks rotation and uploads multipart`, `unsaved prompt on layout switch`, `draft restored after login redirect`, `HTML in labels renders as text` → FAIL → implement → PASS → **commit** `feat(ui): add sensor tree, item palette, properties and background picker`.

### Task 7: Sensors tab
**Files:** `web/src/tabs/SensorsTab.tsx`, `e2e/sensors.spec.ts`
**Behaviour (spec §8.3):** diagnostics banners (text from API; "See the installation guide" note, no install action); grouped table: name, live value + unit, alias inline edit (Enter saves `PUT /api/sensors/alias` `{id, alias}`, Escape cancels, empty clears with null); Roles section with select to pin a sensor or "Automatic" (`PUT /api/roles/{role}` `{sensorId}` / null); search.
- [ ] Failing Playwright: `diagnostics banner`, `alias edit saves with body id`, `clearing alias sends null`, `pin role and reset to automatic`, `values update live` → FAIL → implement → PASS → **commit** `feat(ui): add Sensors tab with aliases, role pins and diagnostics`.

### Task 8: Devices tab
**Files:** `web/src/tabs/DevicesTab.tsx`, `e2e/devices.spec.ts`
**Behaviour (spec §8.4):** each device: name, driver, model code, serial, status (+ message / next retry), rotation select, layout select incl. "None" (PUT `{activeLayout: null}`), mismatch warning; unsupported: "Connected — unsupported model (code XX/YY)" + "Support can be added by contributors; see CONTRIBUTING.md in the project repository."
- [ ] Failing Playwright: `lists connected, retrying and unsupported`, `rotation change PUTs`, `layout None clears active layout`, `mismatch warning` → FAIL → implement → PASS → **commit** `feat(ui): add Devices tab`.

### Task 9: Settings tab
**Files:** `web/src/tabs/SettingsTab.tsx`, `e2e/settings.spec.ts`
**Behaviour (spec §8.5 + owner decision):** Display: °C/°F, 24-hour clock, "Frame-rate cap for animations with text or gauges" (Off, 8, 12, 15, 20, 24, Custom 1–60 → `maxOverlayAnimationFps`, help "Lower values use less CPU; animation speed stays the same"). Access: password set/change (current required when one exists; min 8; confirm), network access toggle (disabled with hint until a password exists), HTTPS toggle, port 1024–65535; after a successful network save show "The page will reload at the new address" and navigate there; 409 → "Port is in use". Logs: download `/api/logs`. About: version (`/api/health`), GPL-3.0, project link text. Logout when authenticated remotely (`/api/auth/state`).
- [ ] Failing Playwright: `unit and clock save`, `fps cap presets/custom/off`, `network toggle disabled without password`, `password change requires current and confirm`, `port conflict message`, `shows version` → FAIL → implement → PASS → **commit** `feat(ui): add Settings tab incl. frame-rate cap and network access`.

### Task 10: Polish, accessibility, real-service smoke
**Files:** as needed; `e2e/a11y.spec.ts`; `docs/testing/hardware-smoke.md` (UI section)
- [ ] Failing Playwright: `all tabs reachable by keyboard`, `controls have accessible names` (role-based lookups), `no horizontal scroll at 360px` (every tab), `focus visible on keyboard navigation` → FAIL → fix → PASS.
- [ ] Owner smoke (controller + owner): `dotnet publish` to `artifacts/run`, restart service (UAC), open `http://localhost:8735`, walk every tab, build a layout by dragging GPU temperature, save, make active, screen updates; enable network access + password and open from a phone; record results and CPU with UI open (informational) in `hardware-smoke.md`.
- [ ] **Commit** `feat(ui): accessibility and responsive polish; record UI smoke results`.

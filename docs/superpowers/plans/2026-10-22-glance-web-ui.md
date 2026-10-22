# Glance Web UI Implementation Plan (Plan 2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The browser UI from spec §8 — tabs Display, Layouts (drag-sensors-from-list editor), Sensors, Devices, Settings, plus login — served by the existing service, with the few API additions it needs.

**Architecture:** Static files in `src/Glance.Service/wwwroot`: plain HTML + CSS + native ES modules, no framework, no build step, no CDN. Pure logic (API client, editor model, schema, formatting) lives in small modules unit-tested with `node --test`; the pages are tested end-to-end with Playwright for .NET driving the installed Edge against a tiny static host with mocked `/api/*` routes. The editor's canvas shows the server-rendered draft (`POST /api/render`) with HTML selection boxes positioned from a new bounds endpoint, so preview and device can never drift.

**Tech Stack:** HTML5, CSS custom properties, ES2022 modules, EventSource (SSE), HTML5 drag-and-drop + pointer events; .NET 8 for API additions (xUnit); Node 24 `node --test` (dev-only, no npm packages); Microsoft.Playwright (Apache-2.0) with `Channel = "msedge"`.

**Spec:** `docs/superpowers/specs/2026-10-21-glance-design.md` (§1, §6.1, §7.1, §8, §9.1). Core API contract from Plan 1 (routes in `src/Glance.Service/Api/*`, `Security/AuthApi.cs`).

## Global Constraints

- Branch `feat/web-ui`. **Commit at the end of every task** (small, meaningful, conventional message ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`). **No push, no PR** until the owner decides.
- Nothing from vendor software in the repo (no vendor names in UI copy except "Thermalright" as supported-hardware fact; no vendor images).
- **No Node.js at runtime, no build step, no framework, no CDN/remote assets** (spec §8; LAN/offline use). Every file the browser loads is in `wwwroot`.
- **Never insert untrusted strings with `innerHTML`/`insertAdjacentHTML`.** Layout names, labels, aliases, sensor names, error messages go through `textContent`/`value`/attributes only. A unit test greps `wwwroot/js` for `innerHTML` and fails on any use.
- All API calls same-origin via `js/api.js`; JSON bodies with `Content-Type: application/json`; uploads `multipart/form-data` (AccessGate rules from Plan 1).
- Clean, calm UI (spec §1): one accent colour, generous spacing, system font stack, dark theme default with light theme via `prefers-color-scheme`; usable from 360 px wide (phones on LAN).
- Live values via one shared EventSource (`/api/events`); preview images refreshed ≤ 1 per second and only while visible (`document.visibilityState`).
- Test commands: `dotnet test Glance.sln --filter "Category!=Hardware"` and `node --test tests/ui-unit`. Playwright tests carry `[Trait("Category","UI")]` and run in the same `dotnet test` (they need Microsoft Edge installed; skip with a clear message if Edge is missing).

## Review Focus

1. **Remote session expires mid-edit (401)** → user is sent to the login page and, after logging in, lands back on the Layouts tab with the unsaved draft restored (draft kept in `sessionStorage`). Test in Task 2 (redirect) and Task 6 (draft restore).
2. **Service restarts / network drops** → a visible "Reconnecting…" banner appears, values grey out instead of showing stale numbers, and everything resumes when SSE reconnects. Test in Task 2.
3. **Unsaved edits when switching layout, tab, or closing the page** → confirm prompt (`beforeunload` + in-app dialog). Test in Task 6.
4. **Server rejects a layout (400 `{errors}`)** → errors shown next to Save, editor stays usable, nothing lost. Test in Task 6.
5. **Rapid edits / dragging** → draft renders are debounced (150 ms) with at most one request in flight; stale responses are discarded. Test in Task 5 (pure scheduler) and Task 6.

---

## File Structure

```
src/Glance.Service/
  Api/UiSupportApi.cs            fonts, timezones, render bounds   (Task 1)
  Api/DevicesApi.cs (modify)     fps, clear active layout           (Task 1)
  Api/EventsApi.cs  (modify)     devices event on settings change   (Task 1)
  wwwroot/
    index.html                   app shell (header, tab bar, <main>, dialogs)
    login.html                   login page
    css/app.css                  design tokens + components
    js/api.js                    fetch wrapper, error model, 401 handling
    js/events.js                 shared EventSource with reconnect + status
    js/dom.js                    tiny safe DOM helpers (el(), clear())
    js/app.js                    hash router, tab mounting, banner
    js/format.js                 value/unit formatting for the UI
    js/tabs/display.js | layouts.js | sensors.js | devices.js | settings.js
    js/editor/model.js           pure layout operations + item defaults
    js/editor/schema.js          per-item-type property descriptors
    js/editor/scheduler.js       debounced single-flight render scheduler
    js/editor/canvas.js          preview image, selection boxes, drag/move, drop
    js/editor/properties.js      property panel generated from schema
    js/editor/sensor-tree.js     live sensor tree (drag source)
    js/editor/background.js      background picker + asset import
    js/login.js
src/Glance.Core/Rendering/OverlayRenderer.cs (modify)  MeasureItems
docs/api.md                       API contract for UI and future clients (Task 1)
tests/ui-unit/*.test.mjs          node --test unit tests
tests/Glance.Ui.Tests/            Playwright (.NET) end-to-end tests + StaticHost + ApiMock
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

### Task 2: App shell, API client, live events, login

**Files:**
- Create: `wwwroot/index.html`, `wwwroot/login.html`, `wwwroot/css/app.css`, `wwwroot/js/{api,events,dom,app,login,format}.js`, `wwwroot/js/tabs/{display,layouts,sensors,devices,settings}.js` (stubs rendering their title), `tests/ui-unit/api.test.mjs`, `tests/ui-unit/no-innerhtml.test.mjs`, `tests/ui-unit/format.test.mjs`, `tests/Glance.Ui.Tests/` project (`StaticHost.cs`, `ApiMock.cs`, `ShellTests.cs`)
- Replace: the placeholder `wwwroot/index.html`

**Interfaces:**
- `api.js`: `export class ApiError extends Error { status; errors /* string[] */ }`; `export async function api(method, path, body?) → parsed JSON | null` (throws `ApiError`; on 401 stores `location.hash` + calls `onUnauthorized()` which navigates to `/login.html?return=<hash>`); `export async function upload(path, formData)`; `export function setUnauthorizedHandler(fn)` (tests).
- `events.js`: `export const live = { on(event, fn) → unsubscribe, status /* 'connecting'|'open'|'reconnecting' */, onStatus(fn) }` — one EventSource; on error shows status `reconnecting` and retries with backoff 1→2→5 s; JSON-parses `data`.
- `dom.js`: `export function el(tag, props = {}, ...children)` (props: `class`, `text`, `attrs`, `on: {event: fn}`; strings children become text nodes), `export function clear(node)`.
- `format.js`: `export function formatValue(value, unit, decimals) → string` (`null`/non-finite → "—"), `export function formatFps(fps)`.
- `app.js`: hash routes `#display` (default), `#layouts`, `#layouts/<name>`, `#sensors`, `#devices`, `#settings`; each tab module exports `mount(container, ctx) → unmount()`; ctx = `{ api, live, navigate(hash), confirmLeave(fn) }`. Header shows a connection banner bound to `live.status`.
- `ApiMock` (C#): routes `/api/**` on a Playwright page to in-memory handlers; seeded with one connected 1280×480 device, sensors (`cpu/t`, `/amdcpu/0/temperature/2`, a GPU group), two layouts, one asset; records requests for assertions. `StaticHost` serves `src/Glance.Service/wwwroot` from the source tree on an ephemeral loopback port.

- [ ] **Step 1: Write failing tests** — node: `api_throws_ApiError_with_errors_array_on_400`, `api_calls_unauthorized_handler_on_401_with_current_hash`, `api_sends_json_content_type`, `formatValue_null_is_em_dash`, `formatValue_rounds_to_decimals`, `no_innerHTML_in_wwwroot_js` (fails on `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`). Playwright: `Tabs_switch_by_hash_and_highlight_active`, `Unauthorized_api_redirects_to_login_with_return`, `Login_posts_password_and_returns_to_hash`, `Reconnecting_banner_shows_when_events_drop_and_hides_on_reconnect` (mock closes the SSE stream).
- [ ] **Step 2: Run** `node --test tests/ui-unit` and `dotnet test --filter Category=UI` — Expected: FAIL.
- [ ] **Step 3: Implement** shell and modules per Interfaces. Design tokens in `app.css` (`--bg`, `--surface`, `--text`, `--muted`, `--accent`, `--danger`, spacing scale 4/8/12/16/24/32, radius 10px); components: `.btn`, `.btn-primary`, `.card`, `.field`, `.banner`, `.tabbar`, `.dialog` (native `<dialog>`).
- [ ] **Step 4: Run** both test commands — Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): add app shell, API client, live events and login`

---

### Task 3: Display tab

**Files:** Modify `wwwroot/js/tabs/display.js`, `css/app.css`; Test `tests/Glance.Ui.Tests/DisplayTabTests.cs`

**Behaviour (spec §8.1):** one card per device: live preview `<img>` (`/api/preview/{key}.jpg?t=<n>` refreshed every 1 s while visible, keeping the last image on 404), name, resolution, status, `fps`, active-layout `<select>` (PUT `activeLayout`), rotation buttons (0/90/180/270 with "Flip 180°" as the prominent one). No devices → empty state "No screen found — plug in a supported display". Device without active layout → first-run call to action: "Choose a layout" (opens select) and "Create a layout" (navigates `#layouts` and starts a new one). Unsupported/retrying devices shown with their message.

- [ ] **Step 1: Failing Playwright tests** — `Shows_device_card_with_preview_and_fps`, `Changing_layout_puts_activeLayout`, `Flip_180_puts_rotation`, `Preview_refresh_pauses_when_page_hidden` (emulate `visibilitychange`; assert no preview requests during 3 s hidden), `Empty_state_when_no_devices`, `First_run_call_to_action_when_no_active_layout`.
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add Display tab with live preview and quick controls`

---

### Task 4: Editor model and property schema (pure modules)

**Files:** Create `wwwroot/js/editor/model.js`, `schema.js`, `scheduler.js`; Test `tests/ui-unit/model.test.mjs`, `schema.test.mjs`, `scheduler.test.mjs`

**Interfaces:**
- `model.js`: `newLayout(name, width, height) → Layout` (colour background `#101820`, no items); `defaultItem(type, x, y, extra?) → Item` for all 10 types (`text, sensor, clock, image, bar, segmentedBar, arcGauge, dial, historyGraph, sparkline`) with unique `id` (`crypto.randomUUID().slice(0,8)`), defaults matching the C# model (font Inter 32/400, colours `#FFFFFF` text, gauge `#4AAFFF` / track `#33FFFFFF`, gauge size 200×200 round / 300×24 bar / 400×150 graph / 160×40 sparkline, windowSeconds 60); `sensorItem(sensorRef, label, x, y)`; `moveItem(layout, id, x, y) → Layout` (clamped to canvas, rounded to integers); `removeItem`, `duplicateItem` (offset +16,+16, new id), `updateItem(layout, id, patch)`; `toLayoutPoint(clientX, clientY, canvasRect, layoutW, layoutH) → {x,y}`; all functions return new objects (no mutation).
- `schema.js`: `fieldsFor(type) → Field[]` where `Field = { key, label, kind: 'text'|'number'|'color'|'select'|'sensor'|'font'|'timezone'|'thresholds'|'bool'|'asset', min?, max?, step?, options? }`; numeric bounds copied from `LayoutLimits.cs` (Plan 1) — the test asserts a hard-coded copy of those bounds so drift is caught.
- `scheduler.js`: `createRenderScheduler(renderFn, { delayMs = 150 }) → { request(layout), dispose() }` — debounced; at most one `renderFn` in flight; while in flight, only the latest requested layout is rendered next; results of superseded calls are dropped (`onResult` only for the newest).

- [ ] **Step 1: Failing node tests** — defaults for every type validate against bounds; `moveItem_clamps_and_rounds`; `toLayoutPoint_scales_css_pixels_to_layout`; `functions_do_not_mutate_input`; `fieldsFor_covers_every_type`; `bounds_match_LayoutLimits`; scheduler: `debounces_rapid_requests_to_one_call`, `never_two_in_flight`, `drops_stale_results` (fake timers via `node:test` mock timers).
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add layout editor model, property schema and render scheduler`

---

### Task 5: Layouts tab — list, canvas, select/move, save, make active

**Files:** Modify `wwwroot/js/tabs/layouts.js`; Create `wwwroot/js/editor/canvas.js`; Test `tests/Glance.Ui.Tests/LayoutsCanvasTests.cs`

**Behaviour:** left column: layout list (+ New, Duplicate, Delete with confirm, rename = Duplicate+Delete flow hidden behind "Rename"); centre: canvas = `<img>` of `POST /api/render` (via scheduler on every edit and every 1 s for live values while visible) with absolutely positioned selection boxes from `POST /api/render/bounds`; click selects, drag moves (pointer events; arrow keys move 1 px, Shift+arrow 10 px; Delete removes; Ctrl/Cmd+D duplicates); canvas scales to fit width keeping aspect; toolbar: Save (PUT layout; shows `{errors}` inline), Make active (device select when >1 device, else the only device), unsaved indicator "●". Layout size mismatch with device canvas → warning text "Made for W×H" (spec §7.1).

- [ ] **Step 1: Failing Playwright tests** — `Selecting_layout_loads_and_renders_preview`, `Drag_moves_item_and_marks_unsaved`, `Arrow_keys_nudge_selected_item`, `Save_puts_layout_and_clears_unsaved`, `Save_errors_are_shown_and_edits_kept` (mock returns 400 `{errors:["windowSeconds must be 1..1800"]}`), `Make_active_puts_device_activeLayout`, `Rapid_drags_issue_single_flight_render_requests` (assert ≤ 1 concurrent `/api/render` in mock), `Size_mismatch_warning_shown`.
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add layout canvas with selection, dragging and saving`

---

### Task 6: Layouts tab — sensor tree, adding items, properties, background, unsaved guard

**Files:** Create `wwwroot/js/editor/{sensor-tree,properties,background}.js`; Modify `layouts.js`; Test `tests/Glance.Ui.Tests/LayoutsEditTests.cs`

**Behaviour (spec §8.2):** sensor tree under the layout list: groups by hardware (`cpu`, `gpu`, `memory`, `storage`, `motherboard`, `other`) with hardware name, live value (SSE), alias shown when set; search box filters; each sensor is draggable — dropping on the canvas adds a `sensor` item at the drop point bound to that sensor id (label = alias or sensor name); a "Roles" group lists role bindings (e.g. "CPU temperature") which drop as `role:…`. Add menu: + Text, + Clock, + Image, + Gauge ▸ (Bar, Segmented bar, Arc gauge, Dial, History graph, Sparkline) — adds at canvas centre, selected. Properties panel for the selected item generated from `schema.js` (sensor field = searchable select of sensors and roles; font = families from `/api/fonts`; timezone from `/api/timezones`; thresholds = editable rows from/colour; asset = picker with thumbnails from `/api/assets`). Background picker: Colour | Image | Animation, asset grid with thumbnails, "Import…" (file input; kind from extension `.png/.jpg/.jpeg` → image, `.gif` → gif, `.zt` → zt with a rotation prompt 0/90/180/270 defaulting to 0, explaining "rotation the file was made for"); upload progress and error message. Unsaved guard: `beforeunload` while dirty; switching layout/tab with unsaved edits → `<dialog>` Save / Discard / Cancel. Draft auto-stash to `sessionStorage` on every edit; restored after re-login (Review Focus 1).

- [ ] **Step 1: Failing Playwright tests** — `Dragging_sensor_onto_canvas_adds_bound_item_at_drop_point`, `Dragging_role_adds_role_binding`, `Sensor_tree_values_update_from_events`, `Search_filters_sensor_tree`, `Add_menu_adds_each_item_type`, `Property_change_updates_layout_and_rerenders`, `Thresholds_editor_adds_and_removes_rows`, `Background_import_zt_asks_rotation_and_uploads_multipart`, `Unsaved_changes_prompt_on_layout_switch`, `Draft_restored_after_login_redirect`, `Labels_with_html_are_rendered_as_text` (alias `<img src=x onerror=alert(1)>` appears literally; no dialog).
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add sensor tree, item palette, properties and background picker`

---

### Task 7: Sensors tab

**Files:** Modify `wwwroot/js/tabs/sensors.js`; Test `tests/Glance.Ui.Tests/SensorsTabTests.cs`

**Behaviour (spec §8.3):** diagnostics banner(s) on top (e.g. PawnIO missing — text from API + "See the installation guide" note, no install action in Plan 2); table grouped by hardware: name, live value with unit, alias (inline edit, Enter saves via `PUT /api/sensors/alias`, Escape cancels, empty clears); Roles section: each role with its resolved sensor and a select to pin another sensor or "Automatic" (`PUT /api/roles/{role}` with `sensorId` or null); search.

- [ ] **Step 1: Failing Playwright tests** — `Diagnostics_banner_shown`, `Alias_edit_saves_with_body_id`, `Clearing_alias_sends_null`, `Pin_role_and_reset_to_automatic`, `Values_update_live`.
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add Sensors tab with aliases, role pins and diagnostics`

---

### Task 8: Devices tab

**Files:** Modify `wwwroot/js/tabs/devices.js`; Test `tests/Glance.Ui.Tests/DevicesTabTests.cs`

**Behaviour (spec §8.4):** every device: name, driver, model code, serial, status (+ message / next retry for retrying), rotation select, active layout select including "None" (PUT `{activeLayout: null}`), layout-mismatch warning; unsupported devices: card "Connected — unsupported model (code XX/YY)" with text "Support can be added by contributors; see CONTRIBUTING.md in the project repository."

- [ ] **Step 1: Failing Playwright tests** — `Lists_connected_retrying_and_unsupported`, `Rotation_change_puts`, `Layout_none_clears_active_layout`, `Mismatch_warning_shown`.
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add Devices tab`

---

### Task 9: Settings tab

**Files:** Modify `wwwroot/js/tabs/settings.js`; Test `tests/Glance.Ui.Tests/SettingsTabTests.cs`

**Behaviour (spec §8.5 + owner decision):** Display: temperature unit (°C/°F), 24-hour clock, **"Frame-rate cap for animations with text or gauges"** (Off, 8, 12, 15, 20, 24, Custom 1–60 → `maxOverlayAnimationFps`) with help text "Lower values use less CPU; animation speed stays the same". Access: password set/change (current required when one exists; min 8; confirm field), network access toggle (disabled with hint until a password exists), HTTPS toggle, port (1024–65535) — saving network shows "The page will reload at the new address" and navigates to the new URL after success; 409 shows "Port is in use". Logs: "Download logs" (`/api/logs`). About: version from `/api/health`, licence GPL-3.0, project link text. Logout button when authenticated remotely (`/api/auth/state`).

- [ ] **Step 1: Failing Playwright tests** — `Unit_and_clock_save`, `Fps_cap_presets_and_custom_and_off`, `Network_toggle_disabled_without_password`, `Password_change_requires_current_and_confirm`, `Port_conflict_shows_message`, `Shows_version`.
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(ui): add Settings tab incl. frame-rate cap and network access`

---

### Task 10: Polish, accessibility, real-service smoke

**Files:** Modify CSS/JS as needed; Create `tests/Glance.Ui.Tests/AccessibilityTests.cs`; Modify `docs/testing/hardware-smoke.md` (UI section)

- [ ] **Step 1: Failing tests** — `All_tabs_reachable_by_keyboard`, `Interactive_controls_have_accessible_names` (every button/input/select has an accessible name via Playwright `GetByRole` lookups), `Layout_fits_360px_width_without_horizontal_scroll` for each tab, `Focus_visible_on_keyboard_navigation`.
- [ ] **Step 2–4:** run (FAIL) → fix → run (PASS).
- [ ] **Step 5: Owner smoke on real hardware** (controller + owner): publish to `artifacts/run`, restart service (UAC), open `http://localhost:8735`, check every tab, build a layout by dragging GPU temperature, save, make active, confirm the screen updates; enable network access from a phone on LAN with a password; record results and CPU with the UI open (informational, no target) in `hardware-smoke.md`.
- [ ] **Step 6: Commit** `feat(ui): accessibility and responsive polish; record UI smoke results`

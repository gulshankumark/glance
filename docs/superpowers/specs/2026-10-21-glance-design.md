# Glance — Design

Date: 2026-10-21 · Status: approved · License: GPL-3.0

## 1. Intent

Thermalright ships small USB LCD screens (for AIO coolers and PC cases) that show
sensor values and themes. Their software, TRCC, is unmaintained: it pins a CPU core
(measured 38% of one core, ~60–70 W extra system draw at idle on a Ryzen 9 9950X),
cannot read new GPUs (no values at all for an Intel Arc Pro B70), is Windows-only
WinForms, and runs elevated with a pile of helper processes.

**Glance** replaces it: one lightweight background service that drives the screen,
reads hardware sensors, and hosts a clean web page to configure everything — from the
same PC or from a phone/laptop on the home network. Open source, so anyone with a
supported screen can use it, and built so other vendors' screens can be added with a
small C# driver.

**Success = people get excited about it.** Not just "it works": install is painless,
the screen comes alive with a good-looking theme before the user clicks anything, the
UI is clean and easy, and the CPU cost is near zero.

## 2. Goals, non-goals, anti-goals

### Goals (v1)
- Windows service that drives a Thermalright 0416:5302 1280×480 screen over HID.
- Sensor readout incl. new GPUs (Intel Arc verified), CPU, memory, disks, fans.
- Web UI (tabs layout, drag-sensors-onto-preview editor) reachable on localhost and,
  opt-in with password, on the LAN.
- Backgrounds: solid colour, still image, GIF, and imported TRCC `.zt` animations.
- Foreground items: sensor values, static text, clocks (local + any time zone), images,
  and gauges/graphs: bar, segmented bar, arc/ring gauge, needle dial, history graph,
  sparkline.
- 4–6 curated themes shipped, original or freely licensed art.
- Installer that sets up the service and fetches PawnIO (see §9).
- Idle cost, measured as the whole service process with the web UI closed (includes
  the 1 Hz sensor poll): ≤2% of one core for a still layout with live sensor values;
  ≤5% for an animated layout with overlay when the frame-rate cap is used (≈10% at full
  24 fps on the owner's Ryzen 9 9950X with the WIC codec); the cap is a user setting. No target while the web UI is open.

### Next (v1.x)
- Linux: same service as a systemd unit, sensors via hwmon, udev rule for device access.
- Additional Thermalright device families as experimental drivers (need testers).

### Non-goals
- Video backgrounds (GIF/.zt only), audio-reactive or screen-mirroring modes.
- Fan/pump control, RGB/LED control.
- Theme marketplace / cloud downloads.
- Runtime-loaded driver plugins or config-file-defined drivers (drivers are C# in the repo).
- Auto-rotating through multiple layouts on a timer.

### Anti-goals (would be a failure even if it works)
- A UI that feels cluttered or technical; first-run that shows an empty/broken screen.
- Any Thermalright proprietary code, decompiled material, themes, images or the
  HWiNFO DLL in the repository or release artifacts.
- A network-reachable, privileged service without authentication.
- Burning CPU when nothing on screen changes.

## 3. Hard constraints

- **Clean repository.** Device protocols are documented in our own words as facts
  (offsets, magic bytes, sizes). No code, text, assets or references copied from TRCC
  or its decompilation. Research material stays outside this repo.
- **Commits/pushes only when the owner asks.**
- .NET 8 (LTS), one codebase for Windows and Linux.
- Dependencies must be GPL-3.0-compatible: LibreHardwareMonitorLib (MPL-2.0),
  HidSharp (Apache-2.0), SkiaSharp (MIT) — all verified working in research spikes.
- PawnIO is never redistributed by us (its installer has no stated redistribution
  terms); it is downloaded from the official source at install time.

## 4. Architecture

One process, ASP.NET Core host, runs as:
- Windows: Windows Service (`UseWindowsService`), LocalSystem — needed for CPU/board
  sensors (admin + PawnIO) and simplest for device access.
- Linux (v1.x): systemd service (`UseSystemd`); device access via udev rule, sensors via
  `/sys/class/hwmon` (mostly readable unprivileged).

Components (in-process, communicating through plain interfaces/events):

| Component | Responsibility |
|---|---|
| **DeviceManager** | Enumerates USB/HID devices, matches them against registered drivers, runs handshakes, reconnects on unplug/replug, owns one `DisplaySession` per connected screen. |
| **Drivers** | One C# class per device protocol (§5). |
| **SensorHub** | Polls the platform sensor source once per second, exposes a snapshot (`id → value, unit, name, hardware`) and availability diagnostics. |
| **LayoutStore** | Loads/saves layouts and settings as JSON on disk, hot-reloads on change, validates. |
| **Renderer** | Per display session: composes background + overlay into device frames (§7). |
| **Web** | Static UI + JSON API + live preview + server-sent events for sensor values (§8). |

No shared memory, no helper processes, no polling loops with tiny sleeps. Frame
timing uses a timer/`PeriodicTimer` per session; the sensor poll is a single 1 Hz loop.

## 5. Device drivers

### 5.1 Interface (shape, names are the builder's call)

```csharp
interface IDisplayDriver
{
    string Id { get; }                       // e.g. "thermalright-hid-5302"
    string DisplayName { get; }
    bool Matches(UsbDeviceInfo dev);         // VID/PID/interface/usage
    Task<DisplayInfo?> ConnectAsync(UsbDeviceInfo dev, CancellationToken ct);
}

interface IDisplayConnection : IAsyncDisposable
{
    DisplayInfo Info { get; }                // width, height, accepted formats, serial, model code
    Task SendFrameAsync(EncodedFrame frame, CancellationToken ct);
}
```

`DisplayInfo` tells the renderer the native resolution and accepted pixel formats
(JPEG, RGB565 LE/BE). Drivers are registered at startup from the assembly; adding a
vendor = adding a class + a protocol doc in `docs/protocols/`.

A connected device whose handshake returns an unknown model code is listed in the
Devices tab as "connected — unsupported model (code XX/YY)" with a link to the
contribution guide, never silently ignored.

### 5.2 Thermalright HID 0416:5302 (v1, verified on hardware)

Facts established by testing on a real device:

- USB composite device "USBDISPLAY", VID 0x0416, PID 0x5302. Interface 0 is a
  vendor-defined HID (input report 37 bytes incl. report ID, output report 513 bytes
  incl. report ID). Interface 1 is a WinUSB bulk interface (not used in v1).
- Every write is one HID output report: report ID `0x00` followed by 512 payload
  bytes; payloads longer than 512 bytes are split into 512-byte chunks, the last chunk
  zero-padded.
- **Handshake** — send a 20-byte message (zero-padded to 512):
  `DA DB DC DD 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00`.
  Reply (input report, byte 0 = report ID 0): bytes 1–4 `DA DB DC DD`, byte 13 = `01`.
  Byte 5 = sub-model, byte 6 = model code. If byte 17 = `0x10`, bytes 21–36 are a
  16-byte serial number. Tested device replied model `0x80`, sub-model `0x01`,
  serial `4250323139343007011D334247022078`.
- **Model code → panel**: `0x80` with sub-model ≤ 2 → 1280×480, JPEG frames.
  Other codes are unknown for v1 (shown as unsupported, see §5.1).
- **Frame** — 20-byte header followed by the JPEG bytes, then chunked as above:

  | Offset | Size | Value |
  |---|---|---|
  | 0 | 4 | `DA DB DC DD` |
  | 4 | 1 | `02` = JPEG |
  | 5 | 3 | `00 00 00` |
  | 8 | 2 | width, u16 LE (1280) |
  | 10 | 2 | height, u16 LE (480) |
  | 12 | 1 | `02` |
  | 13 | 3 | `00 00 00` |
  | 16 | 4 | JPEG length, u32 LE |

- The screen falls back to the vendor logo shortly after frames stop. A frame at least
  once per second keeps it on (verified: 1 fps, no flicker).
- Verified throughput: 24 fps of ~60 KB JPEGs with 2.8% of one core.
- Keep JPEGs below ~450 KB; the vendor software reduces quality above that. Glance
  starts at quality 90 and steps down if a frame exceeds the limit.
- Physical mounting may be upside down: the user's unit needs 180° rotation. Rotation
  is a per-device setting (0/90/180/270), applied by the renderer.

### 5.3 Other Thermalright families (v1.x, experimental)

Research found several other Thermalright protocols (other VID/PIDs, WinUSB bulk and
USB-mass-storage based). None are tested. They will be written up as protocol docs in
our own words and implemented as drivers flagged *experimental* once someone with the
hardware can test. Not in v1 scope.

## 6. Sensors

### 6.1 Model
Each sensor: stable `id`, `hardware` (e.g. "AMD Ryzen 9 9950X"), `name`, `type`
(temperature, load, clock, power, fan, data, voltage…), `unit`, `value`, `available`.
Users can give any sensor a friendly **alias** (motherboard fans come as "Fan #1…").

**Bindings.** A layout item binds to either:
- a **role** — a portable name such as `cpu.temperature`, `cpu.load`, `cpu.clock`,
  `cpu.power.package`, `cpu.fan`, `gpu.load`, `gpu.clock`, `gpu.fan`, `gpu.temperature`,
  `memory.load`, … resolved at runtime per platform (Windows LHM ids, Linux hwmon ids)
  to the first matching sensor; the user can pin a role to a specific sensor in the
  Sensors tab (e.g. which board fan is the CPU fan), or
- a concrete sensor `id` (what the editor writes when a user drags a specific sensor).

Shipped themes use roles only, so they work on any machine and on Linux.

**Missing values.** If a bound sensor is unavailable (PawnIO declined, no discrete GPU,
unpinned fan role), the item still renders its label with `—` as the value. Items are
never hidden or swapped automatically, so layouts don't jump around.

### 6.2 Windows — LibreHardwareMonitorLib 0.9.6
Measured on the owner's machine:

| Hardware | Unelevated | Admin, PawnIO stopped | Admin + PawnIO running |
|---|---|---|---|
| Intel Arc Pro B70 (temp, power, fan, clocks, load, VRAM) | yes | yes | yes |
| Ryzen CPU (Tctl/CCD temps, package & core power, clocks) | no | no | yes |
| Motherboard Super I/O temps & fans | no | no | yes |
| DDR5 DIMM temps | no | no | yes |
| NVMe temps | no | yes | yes |

Full refresh of everything: ~17 ms CPU → <2% of a core at 1 Hz. Only hardware groups
referenced by the active layouts (plus whatever the Sensors tab is showing) need to be
updated each tick.

PawnIO is a demand-start driver; the service must ensure it is started (start the
`PawnIO` service if installed and stopped) before opening LHM.

The Sensors tab shows diagnostics: e.g. "CPU temperatures unavailable — PawnIO driver
not installed [Install]".

### 6.3 Linux — hwmon (v1.x)
`/sys/class/hwmon/*`: `k10temp`/`coretemp` (CPU), `xe`/`i915`/`amdgpu`/`nvidia` paths
(GPU), `nvme`, `spd5118` (DIMMs), `nct6775`/`it87` (board). Load/clock from `/proc`
and `/sys/devices/system/cpu`. Same sensor model and ids scheme (`hwmon:<chip>:<label>`).

## 7. Layouts and rendering

### 7.1 Layout (JSON, one file per layout)
- `name`, `resolution` (designed for, e.g. 1280×480)
- `background`: `{ type: color | image | gif | zt, … }`
- `items[]`, each with `x`, `y`, `font` (family, size, weight), `color`, `align`, and:
  - `sensor`: sensor id, label (optional), unit display, decimals
  - `text`: literal string
  - `clock`: format string, time zone (IANA id or "local"), label
  - `image`: asset reference, size
  - gauge/graph items, all bound to one sensor (role or id), each with `width`,
    `height`, `min`, `max` (or auto), colours, optional label/value text:
    - `bar`: horizontal or vertical fill, rounded or square ends
    - `segmentedBar`: N blocks, colour thresholds (e.g. green < 60 ≤ yellow < 80 ≤ red)
    - `arcGauge`: start/end angle, thickness, value text in centre
    - `dial`: needle, tick marks/labels, start/end angle
    - `historyGraph`: line or area, window length (e.g. 60 s – 30 min), grid on/off
    - `sparkline`: small trend line, window length, no axes

Thresholds (colour by value) are available on all gauge types.

**History.** SensorHub keeps a ring buffer of 1 Hz samples for every sensor referenced
by any layout (and the one currently viewed in the UI), sized to the longest window
in use (max 30 min = 1800 samples per sensor). Kept in memory only; lost on restart.

Per device (in settings, not the layout): active layout, rotation.

**Rotation and resolution.** Rotation describes how the panel is mounted. 0°/180° →
the layout canvas is the panel's native size (1280×480); 90°/270° → the canvas is
portrait (480×1280). Shipped themes are landscape only in v1. A layout whose
resolution doesn't match the device's current canvas is scaled to fit, preserving aspect
ratio, centred on black, and the Devices/Layouts tabs show a "made for W×H" warning.
Units (°C/°F) and 12/24-hour clock are global settings.

Storage: Windows `%ProgramData%\Glance\` (`settings.json`, `layouts/`, `assets/`,
`logs/`); Linux `/var/lib/glance/` + `/etc/glance/`.

### 7.2 Imported media
- **GIF**: decoded once on import into an internal animation (JPEG frames +
  per-frame delays), so playback never decodes GIFs.
- **TRCC `.zt`**: format is `byte 0xDC`, `int32 n`, `int32 cumulativeMs[n]`, then `n` ×
  (`int32 length`, JPEG bytes), little-endian. Frames are already in device orientation
  as the vendor tool wrote them. On import they are treated as device-oriented for the
  device they were made for (the importer asks which rotation they were authored with,
  default 0°) and normalised to layout orientation so all backgrounds behave alike.
  Users import their own files; none are shipped.
  Observed facts: the vendor tool keeps user themes at
  `C:\TRCCCAP\Data\USBLCD\Theme<W><H>\<name>\Theme.zt` (e.g. `Theme1280480\001b\Theme.zt`,
  253 frames, ~41 ms apart, ~60 KB JPEGs). On the owner's unit — a panel that needs 180°
  rotation for Glance-rendered images — these `.zt` frames displayed correctly when sent
  unrotated, i.e. they are device-oriented and must be imported with authored rotation 180°.

### 7.3 Frame pipeline (per display session)
- **Overlay layer** (all items) is rendered once per second, or immediately when the
  layout changes, into a transparent bitmap at layout resolution.
- **Still background**: background + overlay composed once per second, rotated to
  device orientation, JPEG-encoded, sent. If nothing changed, re-send the previous
  encoded frame (keep-alive) without re-rendering.
- **Animated background without items**: pre-rotated JPEG frames are sent as-is at
  their own timing (zero decode/encode).
- **Animated background with items**: decode frame → draw cached overlay → rotate →
  encode → send, at the animation's own timing. Decoded frames may be cached in memory
  if the animation is small enough; otherwise decode per frame. Target ≤5% of one core
  at 24 fps 1280×480 — **must be measured** in the first build; if it misses, cache
  decoded frames or lower the animation frame rate. An optional global cap
  (AppSettings.maxOverlayAnimationFps) skips frames by elapsed time to bound CPU;
  animation speed is preserved.
- The live preview served to the web UI is the latest composed frame in layout
  orientation (what a person sees on the physical screen), not a separate render.

## 8. Web UI

Static assets served by the service; no Node.js at runtime. Framework choice is the
builder's, but the result must feel clean, calm and simple (see §1).

Tabs:
1. **Display (home)** — live preview; device name, resolution, connection state,
   current fps; active layout switcher; one-click "Flip 180°" / rotation; clear next
   steps on first run ("Pick a theme", "Customise").
2. **Layouts** — left column: saved layouts (+ New, Duplicate, Import .zt/GIF/image)
   and below it a live sensor tree with current values. Centre: preview canvas; drag a
   sensor from the tree onto the canvas to place it; drag items to move. Properties of
   the selected item beneath (font, size, colour, label, unit, decimals, time zone for
   clocks). "+ Text", "+ Clock", "+ Image". Background picker. Save / Make active.
   While editing, the canvas shows a server-rendered preview of the unsaved layout
   (posted to a preview endpoint; nothing written to disk). **Save** writes the layout;
   if that layout is active on a screen, the screen updates immediately. **Make active**
   assigns the layout to a screen. No separate draft/published versions.
3. **Sensors** — every sensor with value, alias editing, availability diagnostics.
4. **Devices** — connected screens, driver, model, serial, rotation, assigned layout;
   unsupported devices listed with contribution link. Multiple screens are supported,
   each with its own layout.
5. **Settings** — port, network access + password, HTTPS toggle, units, clock format,
   frame-rate cap for animations with text/gauges on top (off, or 1–60 fps; e.g. 12 or 8),
   about/version, logs download.

API: JSON REST for layouts/settings/devices/sensors, `GET /api/preview/{device}.jpg`,
server-sent events for sensor values and device state.

## 9. Access, security, install

### 9.1 Access
- Binds to loopback by default. Requests from loopback need no login.
- **Network access** is opt-in in Settings; enabling requires setting a password.
  Remote requests require login (password hashed with a slow KDF, cookie session,
  login rate-limited). HTTP by default; optional HTTPS with a generated self-signed
  certificate.
- Uploads: only image/GIF/.zt, size-limited, content-validated, stored under the data
  directory with generated names. No path input from clients is ever used as a file
  path.
- The Windows firewall rule for the port is added only when network access is on.

### 9.2 Windows installer
- Installs the service (LocalSystem, automatic start), data directory, firewall rule
  logic, Start-menu link to `http://localhost:<port>`.
- PawnIO: if not installed, asks, then downloads the official signed installer
  (winget id `namazso.PawnIO`, or the official release download) and runs it. Declining
  is fine: CPU/board sensors show as unavailable.
- Detects running TRCC and asks to close it (both cannot drive the screen at once);
  offers to remove TRCC's logon task.
- On finish, opens the web UI.
- Uninstall removes the service, firewall rule, and optionally the data directory.
  Never uninstalls PawnIO (other tools may use it).

### 9.2a Packaging and release
- Self-contained .NET publish (no separate runtime install), win-x64 for v1.
- **Unsigned** installer and binaries in v1 (owner's decision); code signing is a later
  task. README and release notes explain the SmartScreen "More info → Run anyway" step.
- Distributed via GitHub Releases first; winget later.

### 9.3 Linux (v1.x)
Package with systemd unit and a udev rule granting the service user access to
supported VID/PIDs; data in `/var/lib/glance`.

## 10. Themes

**Art source (owner's decision):** free CC0 images and OFL fonts, selected and laid out
by the builder. Each asset's source URL and license recorded in `THIRD-PARTY-NOTICES`.
Quality bar: each theme should look finished and deliberate on the physical screen —
readable at arm's length, consistent typography, no clutter. The owner reviews themes
on the real device before release.

4–6 curated layouts for 1280×480 at launch, each fully set up with sensor items
(CPU temp/usage/clock/power/fan, GPU usage/clock/fan/temp, local + one world clock)
so the screen looks finished on first run. Themes should show off the gauge types
(e.g. one ring-gauge dashboard, one with history graphs), not only text. Art must be original or under a license
compatible with redistribution (CC0/CC-BY with attribution file). Fonts bundled must be
OFL or similar. Sensor bindings auto-resolve on first run (first CPU, first GPU).

## 11. Open source hygiene

- `LICENSE` GPL-3.0; `THIRD-PARTY-NOTICES` for dependencies, fonts and theme art.
- `docs/protocols/thermalright-hid-5302.md` with §5.2 facts in our own words.
- `CONTRIBUTING.md` with "add a new display" guide: capture USB traffic, write protocol
  doc, implement `IDisplayDriver`, add a framing unit test.
- README positions Glance as an independent project; Thermalright named only to say
  which hardware is supported. No vendor logos.

## 11a. Starting code
The research spikes (HID handshake/frames, LHM + PawnIO probe, SkiaSharp rendering)
proved the approach; every fact they established is recorded in this document. They
live outside the repository and are **not needed** to build Glance — the builder works
from this spec alone and writes the code fresh.

## 12. Testing

- Unit tests: frame/handshake byte building and chunking for each driver; `.zt` and
  GIF import; layout JSON validation; overlay rendering golden images.
- Hardware smoke test (manual, documented): handshake, still layout, animated layout,
  rotation, unplug/replug, CPU usage measurement.

## 13. Decided by the owner
.NET; web server as Windows/Linux service; 1-second updates for sensor values; flexible
backgrounds (still or animated); tabs UI (Display, Layouts, Sensors, Devices,
Settings); drag-sensors-from-list editor; LAN access as well as local; GPL-3.0;
PawnIO downloaded during install; C# drivers for new vendors; curated themes shipped;
clean, easy UI; name "Glance"; nothing proprietary in the repo; no commits until told;
theme art from free CC0 images/OFL fonts curated by the builder; unsigned installer for
v1, signing later.

## 14. Calls made by the designer (check these)
- Single process, LocalSystem service on Windows.
- Loopback needs no login; network access opt-in with password; optional HTTPS.
- Save updates the screen if that layout is active; "Make active" assigns a layout to
  a screen; unsaved edits show only in the editor preview.
- Idle CPU budget measured as whole process, UI closed, sensor poll included (≤2% still,
  ≤5% animated).
- Role-based sensor bindings for themes; missing values render as `—`.
- 90°/270° means portrait canvas; mismatched layouts scale-to-fit with a warning.
- Research spikes stay outside the repo; this spec carries everything they proved.
- Self-contained win-x64 build; GitHub Releases first, winget later.
- Multiple screens supported, each with its own layout.
- GIF and .zt converted on import; .zt rotation asked at import (default 0°).
- JPEG quality 90 with step-down above ~450 KB.
- Sensor aliases; layout items reference stable sensor ids.
- Data in `%ProgramData%\Glance`.
- Installer detects TRCC and offers to close it / remove its autostart.
- History buffer: in-memory, 1 Hz, max 30 min window, not persisted across restarts.
- JPEG codec is pluggable: WIC on Windows (Skia fallback), libjpeg-turbo planned for Linux.
- Frame-rate cap for overlay animations is a user setting (owner decision).

## 15. Left to the builder
Web UI framework and styling approach; installer technology (WiX, Inno Setup, …);
internal project/folder structure; port number default; logging library; exact API
routes; hot-plug detection mechanism; animation frame cache policy within the CPU
target.

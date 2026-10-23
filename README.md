# Glance

An open-source, lightweight replacement for **TRCC**, the Thermalright LCD control software. Glance drives the
small USB LCD screens on Thermalright AIO coolers and cases, reads your hardware sensors, and gives you a clean web
page to design what the screen shows. You can open it on the same PC, or on a phone or laptop on your home network.

> **Status: pre-release.** The core service and web UI work and have been tested on real hardware. There is no
> installer yet (that is the next milestone), so for now you build and run it from source.

## Why Glance exists

TRCC is unmaintained, and it shows:

- It keeps a CPU core busy all the time: **38% of one core** measured at idle, which added **about 60–70 W** to
  system power draw on a Ryzen 9 9950X.
- It cannot read newer GPUs. An Intel Arc Pro B70 shows no values at all.
- It is a Windows-only WinForms app that runs elevated, starts several helper processes, and restarts itself at logon
  through a scheduled task.

Glance is a single background service. It uses almost no CPU, reads current hardware through
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), and is configured from a
browser.

## Glance vs TRCC

### Features

| | TRCC | Glance |
|---|---|---|
| Runs as | Elevated desktop app + helper processes (`TRCC.exe`, `USBLCD.exe`, `USBLCDNEW.exe`, `HWINFO.exe`) | One Windows service |
| Configuration UI | WinForms window on the same PC | Web UI: localhost, or opt-in LAN access with a password (phone friendly) |
| New GPUs (e.g. Intel Arc Pro B70) | No values | Supported via LibreHardwareMonitor |
| Sensors | Misses newer hardware | CPU, GPU, memory, disks, fans, motherboard Super I/O, with friendly names and unit switching (°C/°F) |
| Backgrounds | Own themes and `.zt` animations | Solid colour, still image, GIF, and imported TRCC `.zt` animations |
| Foreground items | Built-in theme elements | Sensor values, static text, clocks (local or any time zone), images, bar, segmented bar, arc/ring gauge, needle dial, history graph, sparkline |
| Layout editor | Desktop editor | Drag sensors onto a live preview, multi-select, snapping with guides, align and distribute |
| Screen rotation | Yes | 0/90/180/270 per screen, separate rotation for imported `.zt` animations |
| Colour calibration | — | Per-screen gamma and brightness with a test pattern, so the panel matches the preview |
| Unplug/replug | — | Screen comes back on its own within seconds |
| Accessibility | — | Every control reachable by keyboard with visible focus |
| Source | Closed | Open source (GPL-3.0) |
| Extending to new screens | Vendor only | Small C# driver behind `IDisplayDriver` |

### Performance

Measured on a Ryzen 9 9950X (32 logical CPUs), Windows 11, Thermalright `0416:5302` 1280×480 screen. CPU is the whole
process as a percentage of **one** core, averaged over 60 s, with the web UI closed. Glance figures include the
1 Hz sensor poll.

| Scenario | TRCC | Glance |
|---|---|---|
| Still layout with live sensors, clocks, an arc gauge and a history graph | **38%** of one core, ~60–70 W extra system draw | **0.91%** of one core (~170 MB) |
| `.zt` animation, ~24 fps, no overlay items | — | **2.92%** (~170 MB) |
| Animation + 3 text items, 12 fps overlay cap | — | **5.26%** (~163 MB) |
| Animation + 3 text items, 8 fps overlay cap | — | **3.93%** (~163 MB) |
| Animation + 3 text items, uncapped 24 fps | — | 9.97% (~163 MB) |

For a still layout that is roughly **40× less CPU** than TRCC. Spread across all 32 logical CPUs, Glance with an
animated layout uses 0.18–0.38% of the machine. The overlay frame-rate cap (`maxOverlayAnimationFps`) is a setting,
so you choose the trade-off between smoothness and CPU.

Full method and raw results: [docs/testing/hardware-smoke.md](docs/testing/hardware-smoke.md).

### Other advantages

- **Near-zero idle cost.** Uses workstation GC and the Windows Imaging Component JPEG encoder; an earlier build
  using Skia's encoder and Server GC used 22.76% CPU and ~810 MB for the same animated layout.
- **Safe by default.** The web server listens on localhost only. LAN access is opt-in, needs a password (salted,
  slow key derivation) and checks request origin.
- **Nothing proprietary.** No TRCC code or assets in the repo. The screen protocol is documented in our own words in
  [docs/protocols/thermalright-hid-5302.md](docs/protocols/thermalright-hid-5302.md).
- **Scriptable.** Everything the UI does goes through a documented REST API ([docs/api.md](docs/api.md)).

## Supported hardware

| Screen | USB ID | Status |
|---|---|---|
| Thermalright 1280×480 LCD (HID) | `0416:5302` | Verified on real hardware |

Other Thermalright screens are detected and shown as "unsupported" with their USB ID. Adding one takes a driver in
`src/Glance.Drivers.Thermalright`.

Some sensors (CPU package temperature, motherboard Super I/O, fans) need admin rights and the
[PawnIO](https://pawnio.eu) driver, as LibreHardwareMonitor requires. Without it Glance still runs and shows the
sensors it can read.

## Build and run

Requirements: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download), Node.js (only to build the UI).

1. Close TRCC first. Exit it from the tray icon, or end `TRCC.exe`, `USBLCD.exe`, `USBLCDNEW.exe` and `HWINFO.exe`
   from an elevated prompt. Two programs cannot drive the screen at once.
2. Build the web UI (output goes to `src/Glance.Service/wwwroot`):

   ```powershell
   cd web
   npm ci
   npm run build
   cd ..
   ```

3. From an elevated terminal, start the service:

   ```powershell
   dotnet run -c Release --project src/Glance.Service
   ```

4. Open <http://localhost:8735>. Until you pick a layout, the screen shows "Glance is running — open
   http://localhost:8735".

Run the tests with `dotnet test` and, for the UI, `npm test` in `web/`.

## Project layout

| Path | What it is |
|---|---|
| `src/Glance.Core` | Layouts, rendering, sensor hub, display sessions, device manager |
| `src/Glance.Drivers.Thermalright` | HID driver for the `0416:5302` screen |
| `src/Glance.Sensors.Windows` | LibreHardwareMonitor sensor source |
| `src/Glance.Platform.Windows` | WIC JPEG codec, data-directory permissions |
| `src/Glance.Service` | ASP.NET Core host, REST API, security, Windows service |
| `web/` | React web UI |
| `tools/Glance.Bench` | CPU and render benchmark |
| `tests/` | Unit, golden-image and service tests |

## Roadmap

- Installer: sets up the service, fetches PawnIO, offers to remove TRCC's autostart.
- Curated built-in themes.
- Linux (systemd service, hwmon sensors).
- More screens, Thermalright and other vendors.

## License

GPL-3.0. See [LICENSE](LICENSE). Third-party packages and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Glance is not affiliated with or endorsed by Thermalright.

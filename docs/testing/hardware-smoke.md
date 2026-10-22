# Hardware smoke test

Manual checks on a PC with a real screen attached. Run after any change to the device, driver, session or service wiring.

## Before you start

1. Stop any other software driving the screen. The vendor app runs elevated, restarts its helper processes and
   relaunches at logon via a scheduled task. Exit it from its tray icon, or end `TRCC.exe`, `USBLCD.exe`,
   `USBLCDNEW.exe` and `HWINFO.exe` from an elevated prompt.
2. From an elevated terminal start the service:
   `dotnet run -c Release --project src/Glance.Service`

## Checks

| # | Check | Expected |
|---|---|---|
| 1 | Start the service | The fallback screen "Glance is running — open http://localhost:8735" shows within 3 s. |
| 2 | Save a layout with `PUT /api/layouts/Smoke` (body: a layout JSON named `Smoke`), then activate it with `PUT /api/devices/<key>` and body `{"activeLayout":"Smoke"}` | The layout is on screen within 2 s. |
| 3 | `PUT /api/devices/<key>` with body `{"activeLayout":"Smoke","rotation":180}` | The picture is rotated 180 degrees and correctly oriented. |
| 4 | Unplug and replug the screen's USB cable | The picture is back within 6 s; the service never crashed. |
| 5 | `GET http://127.0.0.1:8735/api/health` | `{"status":"ok","version":"..."}` |
| 6 | Measure CPU (below) with a still layout and live sensors | At most 2% of one core. |

`<key>` is `thermalright-hid-5302_<serial>`; `GET /api/devices` lists it once the screen connects. The API is at
`http://127.0.0.1:8735`; requests with a body need `Content-Type: application/json`, for example:

```powershell
$base = "http://127.0.0.1:8735"
$key = (Invoke-RestMethod "$base/api/devices")[0].key
Invoke-RestMethod -Method Put "$base/api/layouts/Smoke" -ContentType application/json -InFile smoke-layout.json
Invoke-RestMethod -Method Put "$base/api/devices/$key" -ContentType application/json -Body '{"activeLayout":"Smoke","rotation":180}'
```

Settings and layouts change only through the API while the service runs: `settings.json` is not watched, and the
data directory is readable by SYSTEM and Administrators only.

## CPU measurement

Let the service run for a minute with the still layout on screen, then in PowerShell:

```powershell
$p = Get-Process Glance.Service
$a = $p.TotalProcessorTime; Start-Sleep 60; $p.Refresh(); $b = $p.TotalProcessorTime
"{0:N2} % of one core" -f ((($b - $a).TotalSeconds / 60) * 100)
```

Record the result here: ______ %

## Results

Machine: AMD Ryzen 9 9950X, Intel Arc Pro B70, Windows 11. Device: Thermalright 0416:5302 1280x480 HID, rotation 180.
Release publish, web UI closed. CPU is the Glance process as % of one core over 60 s.

| Scenario | CPU | Working set | Target |
|---|---|---|---|
| Still layout "Smoke" (sensors, clocks, arc gauge, history graph) | 0.91% | ~170 MB | ≤2% ✅ |
| Animation passthrough (253-frame .zt, ~24 fps, rotation 180, no items) | 2.92% | ~170 MB | ≈3% ✅ |
| Animation + 3 text items, uncapped 24 fps (WIC codec) | 9.97% | ~163 MB | ≤5% ❌ (accepted by owner; use the cap) |
| Same, maxOverlayAnimationFps = 12 | 5.26% | ~163 MB | — |
| Same, maxOverlayAnimationFps = 8 | 3.93% | ~163 MB | — |

History: before optimisation, animation plus text was 22.76% with ~810 MB working set. Fixed by switching to workstation GC (from Server GC) and moving the JPEG codec from Skia to WIC.

Also observed:
- Unplug/replug recovered automatically.
- Layout is upright at rotation 180.
- An imported .zt needs `ztRotation=180` on this unit.

Benchmark tool: `dotnet run -c Release --project tools/Glance.Bench -- [stages|session|encode|all] [seconds] [--cap N]`

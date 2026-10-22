# Glance HTTP API

The contract between the Glance service and its web UI (`src/Glance.Service/wwwroot`). Every route lives under
`/api` on the same origin as the UI; the UI calls it only through `web/src/api/client.ts`.

## Conventions

- **JSON**: request and response property names are camelCase and matched case-insensitively on input. Enums are
  camelCase strings (`"celsius"`, `"connected"`). Numbers that are NaN or infinite are sent as `null`.
- **Request bodies** to `/api` must be `Content-Type: application/json` or, for uploads, `multipart/form-data`.
  Any other content type on a POST, PUT, DELETE or PATCH that has a body is rejected with
  `415 {"error":"content type must be application/json or multipart/form-data"}`.
- **Origin**: a mutating request (anything but GET, HEAD, OPTIONS) that carries an `Origin` header must be same-origin
  (same scheme `http`/`https`, host and port as the request); otherwise `403 {"error":"cross-origin request"}`.
  Requests without `Origin` are accepted.
- **Host**: the `Host` header must be `localhost`, the machine name, a loopback address or one of the machine's own IP
  addresses; otherwise `403 {"error":"host not allowed"}` (DNS-rebinding protection). This applies to static files too.
- **Absent vs null**: where noted, a property that is left out of a PUT body means "unchanged" and an explicit `null`
  means "clear".
- **Layout JSON** is the layout file format (see [Layout JSON](#layout-json)); in a body, `type` may appear anywhere in
  an object.

### Error shapes

| Shape | Used by |
| --- | --- |
| `{"error": "message"}` | access rules, devices, settings, sensors, roles, assets, auth |
| `{"errors": ["message", ...]}` | layouts, `/api/render`, `/api/render/bounds` (one entry per validation problem) |

A body that is not valid JSON for a route with a typed body (devices, settings, sensors, roles, auth, duplicate) is a
`400` whose body may be empty; show a generic message for it. A `404` from a route without a stated error body has an
empty body.

## Access and authentication

Applied by `AccessGate` before static files and endpoints, in this order:

1. Host check (above), then Origin and content-type checks for mutating requests (above).
2. **Loopback callers** (127.0.0.1, ::1) may use everything without logging in.
3. Other callers: when network access is off, every request (UI files too) is
   `403 {"error":"network access is disabled"}`.
4. Other callers with network access on: static files are served; `/api` routes need a login cookie, except
   `POST /api/auth/login`, `GET /api/auth/state` and `GET /api/health`. Without one:
   `401 {"error":"login required"}` — the UI then shows the login page.

The login cookie `glance_auth` is HttpOnly, `SameSite=Strict`, lasts 7 days (sliding) and stops working when the
password changes (except for the session that changed it).

| Method | Path | Body | Response |
| --- | --- | --- | --- |
| POST | `/api/auth/login` | `{"password": "..."}` | `200 {"authenticated":true}` + cookie; `401 {"error":"wrong password"}` (also when no password is set); `429` after 5 attempts per minute per address (empty body) |
| POST | `/api/auth/logout` | none | `204` |
| GET | `/api/auth/state` | | `200 {"loopback":bool,"authenticated":bool,"networkAccess":bool}` |

## Health

| Method | Path | Response |
| --- | --- | --- |
| GET | `/api/health` | `200 {"status":"ok","version":"..."}` (open to anonymous callers) |

## Sensors and roles

### GET /api/sensors

Holds the UI-interest lease (every sensor is read while the UI is in use; see [UI interest](#ui-interest)).

```json
{
  "sensors": [
    {"id": "/amdcpu/0/temperature/2", "hardware": "AMD Ryzen 7", "hardwareKind": "cpu", "name": "Tctl",
     "kind": "temperature", "unit": "°C", "value": 61.0, "alias": "CPU die"}
  ],
  "roles": {"cpu.temperature": "/amdcpu/0/temperature/2", "gpu.load": null},
  "diagnostics": [{"code": "...", "message": "..."}]
}
```

- `hardwareKind`: `cpu|gpu|memory|storage|motherboard|other`.
- `kind`: `temperature|load|clock|power|fan|data|smallData|voltage|control|throughput|other`.
- `unit`: Celsius-based unit for the kind (`°C`, `%`, `MHz`, `W`, `RPM`, `GB`, `MB`, `V`, `MB/s`) or `null`.
- `value`: current value or `null` (not read yet, or not finite). `alias`: user alias or `null`.
- `roles`: every role (`cpu.temperature`, `cpu.load`, `cpu.clock`, `cpu.power.package`, `cpu.power.cores`, `cpu.fan`,
  `gpu.temperature`, `gpu.load`, `gpu.clock`, `gpu.memory.clock`, `gpu.memory.load`, `gpu.fan`, `gpu.power`,
  `memory.load`, `memory.used`) mapped to the sensor id it resolves to (pins applied) or `null`.

### PUT /api/sensors/alias

Body `{"id": "<sensor id>", "alias": "text"}`; an empty or missing alias removes it. Trimmed, at most 100 characters.
`204`; `404 {"error":"unknown sensor"}`; `400 {"error":"alias must be at most 100 characters"}`.

### PUT /api/roles/{role}

Body `{"sensorId": "<sensor id>"}` pins the role; `null` or `""` removes the pin. `204`;
`404 {"error":"unknown role"}`; `400 {"error":"unknown sensor"}`.

Layout items refer to a sensor either by id or by role as `"role:<role>"`.

## Layouts

Layout names: 1–64 characters of `A–Z a–z 0–9 space _ -`, no trailing space, not a Windows device name (`CON`,
`NUL`, `COM1`...). An invalid name in the path is `400 {"errors":["invalid layout name"]}`.

| Method | Path | Body | Response |
| --- | --- | --- | --- |
| GET | `/api/layouts` | | `200 [{"name","width","height","error"}]`, sorted case-insensitively. A layout file that cannot be loaded is listed with `width`/`height` 0 and `error` set; otherwise `error` is `null`. |
| GET | `/api/layouts/{name}` | | `200` layout JSON; `404` |
| PUT | `/api/layouts/{name}` | layout JSON (max 2 MB) | `200` (empty) saved; `400 {"errors":[...]}` when it does not parse, `name` differs from the path, or validation fails |
| DELETE | `/api/layouts/{name}` | | `204`; `404` |
| POST | `/api/layouts/{name}/duplicate` | `{"newName": "..."}` | `200` (empty); `400 {"errors":["invalid layout name"]}`; `404` source missing; `409 {"errors":["a layout with that name already exists"]}` |

### POST /api/render

Body: layout JSON (max 2 MB), validated exactly like PUT `/api/layouts/{name}` (`400 {"errors":[...]}`). Returns
`200 image/jpeg` of the layout as sent, at the layout's own size and orientation, background included, with current
sensor values. Writes nothing, sends nothing to a screen. A `?device=` query value is accepted and ignored. Holds the
UI-interest lease.

### POST /api/render/bounds

Body and validation as `/api/render`. Returns the layout-space bounds of every item, keyed by item id, measured with the
same code that draws it:

```json
{"cpuTemp": {"x": 301.5, "y": 112.3, "width": 96.2, "height": 34.9},
 "bar1":    {"x": 10,    "y": 20,    "width": 200,  "height": 30}}
```

- Text, sensor and clock items: the ink bounds of the exact string drawn now (label + current value or time), so a
  value change can change the size. An empty string gives width and height 0.
- Gauges (bar, segmentedBar, arcGauge, dial, historyGraph, sparkline): their declared `x, y, width, height`. Captions
  (value, label) may be drawn outside this box.
- Image items: declared box; a width or height of 0 takes the asset's size (0 when the asset is missing).
- An item that fails to measure is left out. Holds the UI-interest lease.

### Layout JSON

```json
{
  "name": "Gaming", "width": 1280, "height": 480,
  "background": {"type": "color", "color": "#000000"},
  "items": [
    {"type": "sensor", "id": "t1", "x": 40, "y": 40, "sensor": "role:cpu.temperature", "label": "CPU",
     "showUnit": true, "decimals": 0, "font": {"family": "Inter", "size": 48, "weight": 700},
     "color": "#FFFFFF", "align": "left"}
  ]
}
```

- `width`, `height`: 1..4096.
- `background.type`: `color` (`color`), `image` (`assetId`), `animation` (`assetId`; GIF and .zt assets).
- Item `type`: `text`, `sensor`, `clock`, `image`, `bar`, `segmentedBar`, `arcGauge`, `dial`, `historyGraph`,
  `sparkline`. Every item has a unique non-empty `id`, and `x`, `y` in -4096..8192 (top-left; text aligns at `x`).
- Colours: `#RRGGBB` or `#AARRGGBB`. Fonts: `{"family","size","weight"}`, size 4..512, weight 100..900, family
  from `/api/fonts` (an unknown family renders as Inter). `align`: `left|center|right`.
- `text`: `text`, `font`, `color`, `align`.
- `sensor`: `sensor` (required), `label`, `showUnit` (default true), `decimals` 0..3, `font`, `color`, `align`.
- `clock`: `format` (.NET date format, default `HH:mm` or `h:mm tt` by settings), `timeZone` (an id from
  `/api/timezones`, default `local`; an unknown id falls back to local time), `label`, `font`, `color`, `align`.
- `image`: `assetId` (required), `width`, `height` 1..4096.
- Gauges: `sensor` (required), `width`, `height` 1..4096, `min`, `max` (null = automatic; min < max), `color`,
  `trackColor`, `thresholds` (`[{"from": number, "color"}]` ascending by `from`), `showValue`, `label`, `font`.
  - `bar`: `vertical`, `rounded` (default true). `segmentedBar`: `segments` 2..50, `gap` 0..100, `vertical`.
  - `arcGauge`: `startAngle` -360..720, `sweepAngle` 1..360, `thickness` 1..500.
  - `dial`: `startAngle`, `sweepAngle` (as arcGauge), `ticks` 0..100, `needleColor`.
  - `historyGraph`: `windowSeconds` 1..1800, `area`, `grid`. `sparkline`: `windowSeconds` 1..1800.

Validation messages are prefixed with `item '<id>': ` for item problems.

## UI lookups

### GET /api/fonts

`200 {"default": "Inter", "families": ["Inter", "Arial", "Bahnschrift", ...]}` — `Inter` (bundled) first, then the
installed font families, distinct and sorted case-insensitively.

### GET /api/timezones

`200 [{"id": "local", "label": "Local time"}, ..., {"id": "Asia/Tokyo", "label": "(UTC+09:00) Osaka, Sapporo, Tokyo"}, ...]`

`local` first, then each system time zone once under its IANA id (Windows ids converted), sorted by base UTC offset
then label. Every id is valid as a clock item's `timeZone`.

## Devices

### GET /api/devices

```json
[{"key": "thermalright-hid-5302_ABC123", "driverId": "thermalright-hid-5302", "name": "...", "status": "connected",
  "width": 1280, "height": 480, "modelCode": "...", "serial": "...", "message": null, "nextRetry": null,
  "rotation": 0, "activeLayout": "Gaming", "layoutMismatch": false, "fps": 9.8, "framesSent": 1234}]
```

- `status`: `connected|unsupported|retrying`; `message` explains the last problem, `nextRetry` is an ISO time or `null`.
- `key`: `<driverId>_<serial>`, stable across reconnects; use it URL-encoded in paths.
- `width`/`height`: the panel's native size. `rotation`: 0, 90, 180 or 270.
- `activeLayout`: layout name or `null` (the screen shows the built-in status screen).
- `layoutMismatch`: the active layout's size differs from the rotated canvas (width/height swapped for 90 and 270).
- `fps`: frames sent per second over the last 5 s; `framesSent`: frames since the session started. Both 0 without a
  running session.

### PUT /api/devices/{key}

Body `{"rotation": 90, "activeLayout": "Gaming"}`. Each field is optional: absent = unchanged.
`"activeLayout": null` (explicitly present) clears the active layout. Responses: `200` the updated device (as in GET);
`404 {"error":"unknown device"}`; `400 {"error":"rotation must be 0, 90, 180 or 270"}`;
`400 {"error":"layout 'X' does not exist"}`; `400 {"error":"activeLayout must be a layout name or null"}`.

### GET /api/preview/{key}.jpg

`200 image/jpeg`, `Cache-Control: no-store` — the last frame sent to that screen; `404` (empty) before the first frame
or for an unknown key. The UI refreshes it at most once a second and only while visible.

## Settings

### GET /api/settings

`200 {"unit":"celsius","clock24h":true,"maxOverlayAnimationFps":null,"port":8735,"networkAccess":false,"https":false,"hasPassword":false}`

`unit`: `celsius|fahrenheit`. The password hash and certificate password are never returned.

### PUT /api/settings

Body `{"unit": "fahrenheit", "clock24h": false, "maxOverlayAnimationFps": 10}`, each optional (absent = unchanged).
`maxOverlayAnimationFps`: whole number 1..60, or `null` to remove the cap. `200` the settings as in GET;
`400 {"error":"unknown unit"}`, `400 {"error":"maxOverlayAnimationFps must be a whole number or null"}`,
`400 {"error":"maxOverlayAnimationFps must be 1..60 or null"}`.

### PUT /api/settings/password

Body `{"current": "...", "new": "..."}`; `current` is required once a password exists. `new` at least 8 characters.
`200 {"hasPassword":true}` (the caller stays logged in, other sessions end);
`400 {"error":"password must be at least 8 characters"}`; `400 {"error":"current password required"}`;
`403 {"error":"current password is wrong"}`; `429` (rate-limited like login).

### PUT /api/settings/network

Body `{"enabled": true, "https": true, "port": 8735}`; `enabled` required, the others optional. Port 1024..65535.
`200 {"networkAccess":bool,"https":bool,"port":int}` — Kestrel rebinds right after, so the UI may need to reload at
the new address; `400 {"error":"enabled is required"}`; `400 {"error":"port must be 1024..65535"}`;
`400 {"error":"set a password before enabling network access"}`; `409 {"error":"port N is in use by another program"}`.

## Assets

| Method | Path | Body | Response |
| --- | --- | --- | --- |
| GET | `/api/assets` | | `200 [{"id","kind","width","height","frameCount"}]`; `kind` is `image`, `gif` or `zt` |
| POST | `/api/assets` | `multipart/form-data`: `file` (required), `kind` = `image|gif|zt`, optional `ztRotation` = 0/90/180/270 | `200` the new asset (as in the list); `400 {"error":...}` (missing file, bad kind or rotation, unreadable or invalid media); `413 {"error":"file is too large"}` over 64 MB |
| DELETE | `/api/assets/{id}` | | `204`; `404` |
| GET | `/api/assets/{id}/thumbnail.jpg` | | `200 image/jpeg` at most 320 px wide; `404` |

Use `image` assets for `image` backgrounds and items, `gif`/`zt` assets for `animation` backgrounds.

## Logs

`GET /api/logs` → `200 application/zip` attachment `glance-logs.zip` with the service log files.

## Events (SSE)

`GET /api/events` → `200 text/event-stream`, `Cache-Control: no-store`. Messages:

```
event: sensors
data: {"/amdcpu/0/temperature/2":61.0,"cpu/t":null}

event: devices
data: [ ...same array as GET /api/devices... ]
```

- `sensors`: sent immediately on connect, then every second; every known sensor id mapped to its current value or
  `null`.
- `devices`: sent whenever the device list or state changes (connect, disconnect, retry) and whenever settings change
  (rotation, active layout, and any other setting). Bursts are coalesced; the payload is always the full current list.
- No `id:` or `retry:` fields; on disconnect the UI reconnects (EventSource default) and treats values as stale until
  the next `sensors` event.

Use one shared `EventSource` per page.

## UI interest

While the UI is in use the service reads every sensor, not only those the screens need. Interest is held by each open
`/api/events` stream and by a 10-second lease renewed by `GET /api/sensors`, `POST /api/render` and
`POST /api/render/bounds`.

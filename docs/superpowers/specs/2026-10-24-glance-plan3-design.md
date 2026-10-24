# Glance Plan 3 — Design

Windows installer, curated themes, data-directory release blockers, per-field settings repair.

Extends `2026-10-21-glance-design.md` (§9.2 installer, §10 themes). Where this document is
more specific, it wins. Builds on Plan 2 (merged, `df39dc3`).

## 1. Intent

Someone downloads Glance, runs one installer, and a minute later their LCD shows a
finished, informative dashboard and the web UI is open in their browser. No setup, no
editing, no "now pick a layout". First impression is the product.

At the same time, close the two security items parked as **release blockers** in the
Plan 1 follow-ups (I5a, I5b) and stop one bad value in `settings.json` from wiping every
other setting. After Plan 3, Glance on Windows is releasable.

## 2. Goals, non-goals, anti-goals

### Goals
- Inno Setup installer: guided wizard with feature checkboxes, and a fully silent mode.
- Four curated themes ship with Glance; one (Blend) is applied to the screen on first run.
- Two new sensors so the default theme shows everything the owner asked for.
- I5a and I5b fixed, with tests.
- Per-field settings repair, with tests, surfaced in the UI.

### Non-goals
- Linux (packaging, silent install, hwmon) — next plan.
- Code signing, winget publishing (design §9.2a: unsigned v1).
- New layout item types or editor features.
- Themes for resolutions other than 1280×480.

### Anti-goals (failure even if it works)
- Any Thermalright asset, font, image or decompiled code in a theme or the installer.
- An installer that leaves the machine half-installed (service registered but not
  starting, data dir with loose permissions) without saying so.
- A first run that shows the plain "Glance is running" fallback screen instead of a theme.
- Overwriting a layout the user has edited, on install or upgrade.

## 3. Installer (Windows, Inno Setup)

**Tool (owner's decision):** Inno Setup. Free, including commercial use; its licence puts
no terms on Glance's own GPL-3.0 licence. Script lives in the repo (e.g.
`installer/glance.iss`), built from the self-contained `win-x64` publish output.

### 3.1 Guided mode (default)
Wizard pages, in order:
1. Welcome, licence (GPL-3.0).
2. Install location (default `Program Files\Glance`).
3. **Features** (checkboxes):
   - Glance service — required, always checked, greyed out.
   - PawnIO driver (CPU / motherboard sensors) — checked by default; hidden or shown as
     "already installed" when present.
   - Start-menu shortcut — checked.
   - Desktop shortcut — unchecked.
   - Open Glance when setup finishes — checked.
4. If TRCC is running: ask to close it (both cannot drive the screen). Offer to remove
   TRCC's logon task (carried over from design §9.2).
5. Install progress, including PawnIO download/install and service start.
6. Finish → opens `http://localhost:<port>` if checked.

### 3.2 Silent mode
`GlanceSetup.exe /VERYSILENT` (and `/SILENT` for progress-only) installs with the
default feature set and does not open a browser. Features selectable with Inno's
`/COMPONENTS=` / `/TASKS=` (e.g. `/TASKS="!pawnio"` to skip PawnIO). Running TRCC in
silent mode: do not kill it; log and continue (the service will report the screen busy).
Exit code non-zero on failure. Document the switches in the README.

### 3.3 PawnIO
- Downloaded at install time from the official release, never bundled in our installer.
- **Builder's call:** pin URL and SHA-256 of a specific PawnIO release in the script;
  refuse to run a download whose hash does not match.
- Offline or download fails: continue install, finish page / log says CPU and board
  sensors are unavailable until PawnIO is installed, with the link. Not fatal.
- Uninstall never removes PawnIO (other tools use it).

### 3.4 Data directory and service
- Installer creates `%ProgramData%\Glance` **before** the service first runs, with the
  hardened ACL (owner Administrators; SYSTEM + Administrators full control; inheritance
  off). If the directory already exists and is not owned by Administrators/SYSTEM, leave
  it — the service's I5b handling (§6) deals with it. (Designer's call: installer closes
  the window, service remains the authority.)
- Registers the service (LocalSystem, automatic start), starts it, waits for it to
  answer (timeout ~30 s). The installer runs elevated, so on upgrade it reads port and
  scheme from the existing `settings.json`; fresh install uses `http` + 8735. For HTTPS
  the readiness probe accepts the self-signed certificate (loopback only). On timeout: tell the user (guided) / non-zero exit
  (silent), point to the log. Do not roll back silently.
- Upgrade: stop service, replace binaries, start service. Data dir untouched except
  themes (§4.4).

### 3.5 Shortcut URL and firewall (service-owned)
- **Shortcut URL** (Plan 1 follow-ups Task 14 ruling): the Start-menu / desktop
  shortcuts and Finish page must open the URL derived from current settings (scheme +
  port), not a hardcoded link. Users cannot read the hardened data dir, so the
  **service** writes the `.url` shortcut file(s) in the common Start-menu (and desktop,
  if chosen — installer records the choice) on every start and whenever port or HTTPS
  changes. The installer creates the initial shortcut; the Finish page uses the same
  derivation as the readiness check.
- **Firewall** (parent design §9.1, owner's earlier decision): the rule exists only while
  network access is on. The **service** (LocalSystem) adds/updates/removes it at runtime
  — on start and on changes to `networkAccess` or `port`. Rule has a fixed name so it can
  be found; uninstall removes it. This is new service work with tests (rule logic behind
  an interface; real `netsh`/COM call smoke-tested on hardware).
- Uninstall: stop and remove service, firewall rule; ask (guided) whether to delete the
  data directory; silent uninstall keeps it.

## 4. Themes

### 4.1 Set
Four themes, 1280×480, all showing the same data (§5):

| Name | Look |
|---|---|
| **Blend** (default) | Dark radial background. Arc gauges for CPU temp and GPU temp, a sparkline/history line under each, detail text (power, clock, load / power, core clock, VRAM GB). Right column: large clock, date, RAM bar with used + free. One accent colour per device (CPU warm red, GPU cyan, RAM violet). |
| **Clean** | Three columns, big numbers, thin divider rules, calm colours. |
| **Gauges** | Arc rings for CPU and GPU, clock/date and a RAM bar. Dashboard feel. |
| **Live graphs** | Monospace (JetBrains Mono), history graphs for CPU and GPU, numbers on top, clock + RAM on the right. |

Mockups the owner chose from: `.superpowers/brainstorm/914-1792826911/content/theme-styles.html`
and `hybrid.html` (option 1 = Blend). These guide the look; exact positions, sizes and
colours are the builder's, held to the quality bar of design §10 (readable at arm's
length, deliberate, no clutter). Owner reviews all four on the real screen before release.

### 4.2 Data shown (owner's decision)
- CPU: temperature, package power, clock, load.
- GPU: temperature, core clock, power, VRAM used (GB, with total) — or VRAM % where a
  theme has no room.
- RAM: used and available (GB).
- Date and time (respecting `clock24h`).

Uses existing item types only: `arcGauge`, `sparkline`, `historyGraph`, `bar` /
`segmentedBar`, `sensor`, `clock`, `text`.

### 4.3 Assets and fonts
- Fonts: Inter (already embedded) and **JetBrains Mono (OFL)** — embed it like Inter.
  `FontProvider` currently resolves non-Inter families from the system; extend it to
  serve embedded JetBrains Mono first.
- Backgrounds: colour/gradient only, or CC0 images. No Thermalright art.
- Every bundled font/image recorded in `THIRD-PARTY-NOTICES` with source URL and licence.

### 4.4 Shipping and first run
- Themes ship as layout JSON (and any assets) in the install directory. The **service**
  seeds them into `<data>/layouts` on start (covers install, upgrade and dev runs) and
  never overwrites an existing layout of the same name.
- **Deletion sticks:** the service records which theme names it has seeded (in settings
  or a marker file). A seeded theme the user deleted is not copied back; a theme that is
  new in an upgrade is seeded once. After a quarantine (§6.2) the record is gone with
  the old settings, so all themes seed again.
- **Default layout:** whenever seeding runs, every device whose `ActiveLayout` is `null`
  **and** that has never had the default applied gets **Blend** (covers newly seen
  devices and devices already known from a Plan 2 install). Record per device that the
  default was applied, so a user who later clears the layout in the UI keeps the
  fallback screen. Devices with a layout chosen are never touched.
- Sensor bindings use roles (`cpu.temperature`, …) so they resolve on any machine.

## 5. New sensors

| Role | Source (LHM) | Unit |
|---|---|---|
| `memory.available` | Memory → Data "Memory Available" | GB |
| `gpu.memory.used` | GPU → SmallData "GPU Memory Used" (LHM reports MB) | GB |

Add to `Roles` and `LhmRoleMap`, same pattern as `memory.used` / `gpu.memory.load`.
Missing on a machine → shows as unavailable like any other role. Unit conversion for
VRAM is the builder's (convert at the source or format in the item — stay consistent
with how `memory.used` is shown).

## 6. Release blockers: data directory

Context: when running as LocalSystem the service hardens `%ProgramData%\Glance`
(`DataDirectoryAcl`). Parked in the Plan 1 follow-ups as Plan 3 release blockers.

### 6.1 I5a — interrupted reset leaves children untrusted
**Problem:** `Enforce` returns `AlreadySecure` when the root alone looks hardened. If a
previous run took ownership and crashed during `ResetChildren`, children it had not
reached keep foreign owners / explicit ACEs forever.

**Fix:** on every enforcement, after the root check, walk the tree (same reparse-point
rules as today: links are deleted, never followed) and reset any entry whose owner is not
trusted or that has explicit (non-inherited) ACEs. The directory is small; cost is fine.
`AlreadySecure` only when root **and** every child is clean.

A child with an untrusted owner **under an already-hardened root** is only reset (owner
→ Administrators), **not** a quarantine trigger: only SYSTEM/Administrators can create
entries there, so such a file comes from an elevated admin (e.g. a layout copied in via
Explorer), not an attacker. Quarantine (§6.2) triggers only on root takeover or an
unfinished takeover (§6.3).

**Tests:** root hardened + one child with a foreign explicit ACE → child reset, outcome
is not `AlreadySecure`. Root hardened + child with untrusted owner → owner reset, no
quarantine.

### 6.2 I5b — planted files trusted after takeover
**Problem:** a local user creates `%ProgramData%\Glance` before install and plants
`settings.json` (e.g. their password hash, `networkAccess: true`), `keys/`, `https.pfx`,
`kestrel.json`. On takeover the ACLs are fixed but the contents are trusted.

**Fix (owner's decision: quarantine, start with defaults, tell the user):**
- When enforcement had to take ownership (`OwnershipTaken`) — or finds an unfinished
  takeover (§6.3) — move every existing entry in the data dir (except the new quarantine
  folder) into `quarantine-<UTC timestamp>/` inside the data dir, then continue with an
  empty, hardened data dir. Settings, keys and certificate regenerate as on a fresh
  install. Layouts and media are quarantined too (designer's call: they are
  attacker-controllable input; themes re-copy per §4.4).
- Log a warning: what happened, why, quarantine path.
- Expose a startup notice (same mechanism as §7's repair notice) so the web UI shows a
  banner: "Glance found files in its data folder it couldn't trust and moved them to
  `quarantine-…`. Settings were reset; set your password again if you use network
  access." Banner is dismissible; dismissal persists per notice.
- Quarantine folder is hardened like the rest and never auto-deleted.
- Dev paths (`GLANCE_DATA` override) are skipped, as today.

**Tests:** takeover with planted `settings.json`/`keys` → files end up under
`quarantine-*`, service starts with default settings, notice raised. No takeover → nothing
moved.

### 6.3 Crash safety of the takeover
A crash between "ownership taken" and "contents quarantined" must not leave planted
files trusted on the next start (the I5a flaw one step later). Order:
1. Take ownership of the root and harden its DACL (root only; children untouched).
2. Write a `takeover-pending` marker in the root.
3. Move every other entry into `quarantine-<timestamp>/` (moves within one volume are
   renames; contents are never opened or trusted).
4. Harden the quarantine folder tree.
5. Delete the marker, then continue normal startup (fresh settings, keys, themes).

On start, a present marker means a takeover did not finish: resume from step 3 (anything
outside existing `quarantine-*` folders and the marker gets quarantined). Test: simulate
a crash after step 2 and after step 3 → the next run ends with the planted files
quarantined and the notice raised.

### 6.4 Plan 1 deferred hardening items (in scope)
From the Plan 1 follow-ups ("deferred, Plan 3"), done with I5a/I5b because they touch
the same walk:
- **Hard links:** a file with link count > 1 is never re-ACL'd or re-owned in place (that
  would change the security of a target outside the data dir). It is deleted (under
  takeover: the link is removed, not moved). Test with a planted hard link.
- Reparse handling limited to symlink / mount-point tags; links removed before the root
  DACL is set.
- `Enforce` failures also go to the service's file log (not only the Event Log), so the
  installer's "see the log" points somewhere a user can find.
- LoopbackFallback message shows the configured port; its self-probe 409 fixed.
- Run the elevated ACL test on the real machine (part of the hardware smoke).

## 7. Per-field settings repair

**Problem:** `SettingsStore.Load` treats any deserialize or validation error as
"whole file bad" → all defaults (port, password hash, every device's layout/rotation/
gamma lost).

**Fix:**
- Not valid JSON / not an object → as today: copy to `.corrupt`, all defaults, notice.
- Valid JSON object → start from `new AppSettings()` and apply each top-level property
  on its own. A property that fails to deserialize or fails its check keeps its default.
- `devices`: per device key, per field. A bad `gamma` resets only that device's gamma;
  a device entry that is not an object resets that device only. Same idea for
  `aliases` / `rolePins` entries (drop the bad entry).
- Unknown properties: ignored (as today).
- If anything was repaired: copy the original to `.corrupt`, save the repaired file,
  set `LoadError` to a readable list ("devices.0416:5302.gamma: must be 0.5..3 → reset
  to 1"), and raise the UI notice.
- Validation rules stay in one place (`AppSettings.Validate` / `DeviceSettings`); the
  builder may split them per field so the loader and `Validate` share them.

**Tests:** one per field kind (port, fps, unit enum, device rotation/gamma/brightness,
non-object device, null dictionaries), each asserting the good fields survive. Invalid
JSON still yields defaults.

## 8. UI notices

One small mechanism serves §6.2 and §7: the service holds a list of startup notices
(id, severity, text); an API returns them; the web UI shows a banner per notice with a
dismiss button. Dismissal stored in settings (or a small file) by notice id. Builder's
call on shape; keep it minimal.

## 9. Work order (small commits)

1. Sensors: `memory.available`, `gpu.memory.used`.
2. Settings per-field repair + notices API + banner.
3. I5a, plus §6.4 hardening items (one commit each).
4. I5b quarantine with crash-safe marker (§6.3), on top of notices.
5. Service-owned firewall rule and shortcut URL (§3.5).
6. Embedded JetBrains Mono.
7. Theme seeding (deletion sticks) + default-layout rule.
8. Themes, one commit each.
9. Installer script, then silent mode, PawnIO, TRCC handling, uninstall.
10. Hardware smoke on the real screen (incl. elevated ACL test); owner reviews themes.

## 10. Decided by the owner
- Plan 3 is Windows only; Linux next.
- Inno Setup.
- Guided installer with feature checkboxes, plus silent mode.
- Default data: CPU temp/power/clock/load, GPU temp/core clock/power/VRAM, RAM used and
  available, date/time.
- Four themes: Blend (default), Clean, Gauges, Live graphs.
- I5b: quarantine and start with defaults, with a warning to the user.

## 11. Calls made by the designer (check these)
- Pin PawnIO URL + SHA-256; offline install continues without PawnIO.
- Installer pre-creates the data dir hardened; service stays the authority.
- Silent mode does not close TRCC.
- Silent uninstall keeps the data dir.
- Quarantine includes layouts and media, not just settings/keys.
- Quarantine only on root takeover / unfinished takeover; a foreign-owned child under a
  hardened root is just reset (it can only come from an admin).
- Takeover is crash-safe via a `takeover-pending` marker.
- Plan 1 deferred hardening items (hard links, reparse tags, file log, fallback port) are
  in Plan 3.
- Service owns the firewall rule and rewrites the shortcut URL at runtime.
- Devices with no layout get Blend once (incl. existing Plan 2 devices); clearing it
  later sticks. Deleted themes stay deleted.
- One notice mechanism shared by settings repair and quarantine; banners dismissible.

## 12. Left to the builder
Exact theme geometry and colours; where theme copying lives; VRAM unit conversion
point; notice API shape; Inno script structure; test helpers.

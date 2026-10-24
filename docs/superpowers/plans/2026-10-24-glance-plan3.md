# Glance Plan 3 (Windows installer, themes, release blockers) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows user downloads one installer from a GitHub Release, installs (guided or silent), and a minute later the LCD shows the Blend theme and the web UI opens; the data directory release blockers I5a/I5b are fixed.

**Architecture:** Service-side work (sensors, settings repair, notices, ACL hardening, firewall, shortcuts, theme seeding) lands first, each behind tests. The installer is an Inno Setup script that leans on small CLI modes of `Glance.Service.exe` for anything security- or settings-sensitive, so URL derivation and ACL logic live in one place (C#). A GitHub Actions workflow builds, tests, packages and releases on an owner-pushed `v*` tag.

**Tech Stack:** .NET 8 (`net8.0` / `net8.0-windows`), xUnit, ASP.NET Core minimal APIs, SkiaSharp, LibreHardwareMonitorLib, React + Vite + Vitest (web), Inno Setup 6, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-24-glance-plan3-design.md` (extends `docs/superpowers/specs/2026-10-21-glance-design.md`). Read both.

## Global Constraints

- Branch `feat/plan3-installer-themes`. One small commit per step marked Commit; never one big commit.
- Never `git push`, never create or push tags. The owner does both.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Zero Thermalright/TRCC decompiled code or assets in the repo. Themes, fonts, images: original work, CC0 or OFL only, each listed in `THIRD-PARTY-NOTICES.md`.
- `TreatWarningsAsErrors` is on; the build must stay warning-free.
- Windows only. Linux is out of scope.
- Default port `8735`; data dir `%ProgramData%\Glance` (`GLANCE_DATA` overrides, and an override disables ACL enforcement, as today).
- Hardened ACL: owner Administrators; SYSTEM + Administrators full control; inheritance off.
- Layout names must pass `LayoutValidator.IsValidName` (`^[A-Za-z0-9 _-]{1,64}$`).
- Themes: 1280×480. Names exactly `Blend`, `Clean`, `Gauges`, `Live graphs`. Blend is the default.
- PawnIO is downloaded at install time with a pinned URL + SHA-256; never bundled; never uninstalled.
- Installer unsigned (spec §9.2a of parent design). Release ships `Glance-Setup-<version>.exe` + `SHA256SUMS.txt`.

## Review Focus

1. **Upgrade over an existing Plan 2 install that already has a layout named `Blend`** — the user's layout is kept, never overwritten; the device keeps its active layout. Test in Task 11.
2. **Port changed while network access is on** — the firewall rule moves to the new port; there is never more than one Glance rule. Test in Task 9.
3. **HTTPS on but the service fell back to loopback** — shortcuts and Finish page open `http://localhost:8735`, not the configured `https://…:<port>`. Test in Task 8.
4. **Valid JSON with a `null` or wrongly typed top-level field (e.g. `"devices": null`, `"port": "abc"`)** — only that field resets; password hash and other devices survive. Test in Task 2.
5. **A junction planted in the data dir pointing at `C:\Windows`** — the junction itself is removed; the target's ACL is never touched. Test in Task 5.

---

## File map

| Area | Files |
|---|---|
| Sensors | `src/Glance.Core/Sensors/Roles.cs`, `src/Glance.Sensors.Windows/LhmRoleMap.cs`, `src/Glance.Sensors.Windows/LhmSensorSource.cs` |
| Settings repair | Create `src/Glance.Core/Layouts/SettingsRepair.cs`; modify `SettingsStore.cs` |
| Notices | Create `src/Glance.Core/Notices/NoticeBoard.cs`, `src/Glance.Service/Api/NoticesApi.cs`, `web/src/ui/NoticeBanner.tsx` |
| ACL | `src/Glance.Platform.Windows/DataDirectoryAcl.cs` (+ new `FileLinks.cs` for P/Invoke) |
| Service address / shortcuts | Create `src/Glance.Service/ServiceAddress.cs`, `src/Glance.Platform.Windows/UrlShortcuts.cs` |
| Firewall | Create `src/Glance.Platform.Windows/IFirewall.cs`, `NetshFirewall.cs`, `src/Glance.Service/FirewallSync.cs` |
| CLI modes | Create `src/Glance.Service/Cli.cs`; modify `Program.cs` |
| Fonts | `src/Glance.Core/Fonts/JetBrainsMono-*.ttf`, `FontProvider.cs`, `UiSupportApi.cs` |
| Themes | Create `src/Glance.Service/themes/*.json`, `src/Glance.Core/Layouts/ThemeSeeder.cs`; modify `DeviceManager.cs`, `AppSettings.cs` |
| Installer | Create `installer/glance.iss`, `installer/release-notes.md` |
| Release | Create `.github/workflows/release.yml`; modify `Directory.Build.props`, `README.md` |

---

### Task 1: Sensors `memory.available` and `gpu.memory.used`

**Files:**
- Modify: `src/Glance.Core/Sensors/Roles.cs`, `src/Glance.Sensors.Windows/LhmRoleMap.cs`, `src/Glance.Sensors.Windows/LhmSensorSource.cs`
- Test: `tests/Glance.Sensors.Windows.Tests/LhmRoleMapTests.cs`, `tests/Glance.Sensors.Windows.Tests/LhmSensorSourceTests.cs`

**Interfaces:**
- Produces: `Roles.MemoryAvailable = "memory.available"`, `Roles.GpuMemoryUsed = "gpu.memory.used"`, both in `Roles.All`.
- Produces: all LHM `SmallData` sensors (MB) are reported as `SensorKind.Data` with value ÷ 1024 (GB). This is the single conversion point (spec §5).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Memory_available_maps_to_total_memory_data_sensor()
// tree: hardware "Total Memory" kind memory, Data sensor "Memory Available" → role resolves to its id

[Fact] public void Gpu_memory_used_maps_to_gpu_small_data_sensor()
// tree: gpu hardware, SmallData "GPU Memory Used" → Roles.GpuMemoryUsed resolves to its id

[Fact] public void Small_data_is_reported_in_gigabytes_as_data()
// fake LHM sensor SmallData value 8192 → SensorInfo.Kind == SensorKind.Data, snapshot value == 8.0
```

- [ ] **Step 2:** Run `dotnet test tests/Glance.Sensors.Windows.Tests --filter "FullyQualifiedName~LhmRoleMapTests|FullyQualifiedName~LhmSensorSourceTests"` → new tests FAIL.
- [ ] **Step 3:** Add the two roles; rules `new(Roles.MemoryAvailable, "memory", SensorKind.Data, new[] { "Memory Available" }, "Total Memory")` and `new(Roles.GpuMemoryUsed, "gpu", SensorKind.Data, new[] { "GPU Memory Used" })` (Data, because of the conversion). In `LhmSensorSource`, map `SensorType.SmallData` to `SensorKind.Data` and divide its readings by 1024 where values are read. Update `MapSensorKind` tests that expected `SmallData`.
- [ ] **Step 4:** Run the same command → PASS; then `dotnet test` (all) → PASS.
- [ ] **Step 5: Commit** `feat(sensors): add memory.available and gpu.memory.used roles; report VRAM in GB`

---

### Task 2: Per-field settings repair

**Files:**
- Create: `src/Glance.Core/Layouts/SettingsRepair.cs`
- Modify: `src/Glance.Core/Layouts/SettingsStore.cs`
- Test: `tests/Glance.Core.Tests/Layouts/SettingsRepairTests.cs`, `tests/Glance.Core.Tests/Layouts/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `public static (AppSettings Settings, IReadOnlyList<string> Repairs) SettingsRepair.Load(string json)` — throws `JsonException` only when the text is not a JSON object.
- Produces: `SettingsStore.LoadError` (string?, unchanged type) now holds the repairs joined with `"; "`; new `SettingsStore.Repairs` (`IReadOnlyList<string>`).

Algorithm (generic, so later fields are covered automatically): start from `new AppSettings()`. For each top-level property of the JSON object: deserialize `{ "<prop>": <value> }` onto a copy with `GlanceJson.Options`; keep it only if deserialization succeeds and `Validate()` reports no new error; otherwise record `"<prop>: <reason> → default"`. For `devices`, `aliases`, `rolePins`, do the same per entry (`"devices.<key>.<field>"` granularity for devices: try each device field on a default `DeviceSettings`; a device that is not an object is dropped and recorded). Reasons reuse existing texts (`DeviceSettings.GammaError`, etc.).

- [ ] **Step 1: Write failing tests** (one per field kind, each asserting good fields survive)

```csharp
[Fact] public void Bad_port_resets_only_port()          // {"port":"abc","passwordHash":"h"} → Port==8735, PasswordHash=="h", repairs contain "port"
[Fact] public void Out_of_range_fps_resets_only_fps()   // maxOverlayAnimationFps: 0
[Fact] public void Unknown_unit_enum_resets_unit()      // "unit":"Kelvin"
[Fact] public void Bad_gamma_resets_only_that_devices_gamma()
// devices: {"0416:5302":{"gamma":9,"rotation":90},"0416:8001":{"gamma":2}} → first: Gamma 1.0, Rotation 90; second: Gamma 2; repair text contains "devices.0416:5302.gamma"
[Fact] public void Bad_rotation_and_brightness_reset_per_device()
[Fact] public void Non_object_device_is_dropped_others_kept()
[Fact] public void Null_dictionaries_become_empty()     // "devices":null,"aliases":null → empty, no throw
[Fact] public void Clean_file_reports_no_repairs()
```

In `SettingsStoreTests`: `Repaired_file_is_saved_and_original_kept_as_corrupt` — write a file with bad gamma; construct store; assert `settings.json.corrupt` holds the original text, the saved file has gamma 1, `LoadError` mentions `gamma`. Keep `Corrupt_file_falls_back_to_defaults_without_throwing` passing (invalid JSON → defaults).

- [ ] **Step 2:** `dotnet test tests/Glance.Core.Tests --filter "FullyQualifiedName~SettingsRepair|FullyQualifiedName~SettingsStoreTests"` → FAIL.
- [ ] **Step 3:** Implement `SettingsRepair.Load`; in `SettingsStore` load path call it; on non-empty repairs copy original to `.corrupt` (overwrite), save repaired via `AtomicFile`, set `Repairs`/`LoadError`.
- [ ] **Step 4:** Same command → PASS; full `dotnet test` → PASS.
- [ ] **Step 5: Commit** `feat(settings): repair settings.json per field instead of resetting everything`

---

### Task 3: Startup notices (API + web banner)

**Files:**
- Create: `src/Glance.Core/Notices/NoticeBoard.cs`, `src/Glance.Service/Api/NoticesApi.cs`, `web/src/ui/NoticeBanner.tsx`, `web/src/ui/NoticeBanner.test.tsx`
- Modify: `src/Glance.Core/Layouts/AppSettings.cs` (add `List<string> DismissedNotices`), `src/Glance.Service/Program.cs`, `web/src/api/client.ts`, `web/src/App.tsx`
- Test: `tests/Glance.Service.Tests/NoticesApiTests.cs`

**Interfaces:**
- Produces: `public sealed record Notice(string Id, string Severity, string Text);` (`Severity` is `"warning"` or `"error"`).
- Produces: `NoticeBoard` (singleton): `void Add(Notice n)`, `IReadOnlyList<Notice> Active` (excludes ids in `settings.DismissedNotices`), `void Dismiss(string id)` (adds to `DismissedNotices` via `SettingsStore.Update`).
- Produces: `GET /api/notices` → `[{ id, severity, text }]`; `POST /api/notices/{id}/dismiss` → 204; both behind the existing auth gate.
- Consumes: `SettingsStore.Repairs` (Task 2). At startup, if non-empty, `Add(new Notice("settings-repaired-<UTC yyyyMMddHHmmss>", "warning", "Some settings were invalid and were reset: <LoadError>. The original file is settings.json.corrupt."))`.

- [ ] **Step 1: Failing tests** — `Get_returns_added_notice`, `Dismiss_hides_notice_and_persists` (new factory instance with same data dir still hides it), `Notices_require_auth_when_network_access_on` (reuse `SecurityTests` pattern), `Repaired_settings_raise_a_notice_at_startup` (seed bad gamma via `GlanceFactory`). Web: `NoticeBanner.test.tsx` — renders one banner per notice; clicking Dismiss calls `dismissNotice(id)` and removes it.
- [ ] **Step 2:** `dotnet test tests/Glance.Service.Tests --filter "FullyQualifiedName~NoticesApiTests"` and `cd web && npx vitest run src/ui/NoticeBanner.test.tsx` → FAIL.
- [ ] **Step 3:** Implement board, API (map in `Program.cs` beside `MapUiSupportApi`), client functions `getNotices(): Promise<Notice[]>` / `dismissNotice(id: string): Promise<void>`, banner at top of `App.tsx` (fetch on mount). Banner text is rendered as text, never HTML.
- [ ] **Step 4:** Both commands → PASS; `dotnet test` and `cd web && npm test` → PASS.
- [ ] **Step 5: Commit** in two commits: `feat(service): startup notices API` then `feat(ui): notice banners`.

---

### Task 4: I5a — reset untrusted children even when the root is already hardened

**Files:**
- Modify: `src/Glance.Platform.Windows/DataDirectoryAcl.cs`
- Test: `tests/Glance.Platform.Windows.Tests/DataDirectoryAclTests.cs`

**Interfaces:**
- Produces (replaces the `Outcome` return): `public sealed record AclResult(Outcome Outcome, string? QuarantineDir, IReadOnlyList<string> Actions);` returned by `DataDirectoryAcl.Enforce(string root)`. `Outcome` gains `Quarantined` (used in Task 6). `Actions` are human-readable lines for the log.
- Rule (spec §6.1): when the root is hardened and owned by SYSTEM/Administrators, walk the tree (no reparse following); any entry not owned by SYSTEM/Administrators or carrying explicit (non-inherited) ACEs is reset (owner Administrators, explicit ACEs dropped, inheritance on). If anything was reset, outcome is `Tightened`, not `AlreadySecure`. No quarantine in this case.

- [ ] **Step 1: Failing tests** (tests that need elevation use the existing skip-unless-elevated pattern in this file):
  - `Hardened_root_with_child_owned_by_user_resets_child` — outcome `Tightened`, child owner Administrators, no explicit ACEs.
  - `Hardened_root_with_child_explicit_ace_resets_child`.
  - `Hardened_root_with_clean_children_is_AlreadySecure`.
- [ ] **Step 2:** `dotnet test tests/Glance.Platform.Windows.Tests --filter "FullyQualifiedName~DataDirectoryAclTests"` (from an elevated shell) → FAIL.
- [ ] **Step 3:** Implement; update `Program.cs` to log `result.Actions` and switch on `result.Outcome`.
- [ ] **Step 4:** Same command → PASS.
- [ ] **Step 5: Commit** `fix(acl): reset untrusted children under an already hardened data dir (I5a)`

---

### Task 5: ACL hardening items from Plan 1 (spec §6.4)

**Files:**
- Create: `src/Glance.Platform.Windows/FileLinks.cs`
- Modify: `src/Glance.Platform.Windows/DataDirectoryAcl.cs`, `src/Glance.Service/Program.cs`, `src/Glance.Service/Security/NetworkConfig.cs` (fallback messaging)
- Test: `tests/Glance.Platform.Windows.Tests/DataDirectoryAclTests.cs`, `tests/Glance.Platform.Windows.Tests/FileLinksTests.cs`, `tests/Glance.Service.Tests/NetworkConfigTests.cs`

**Interfaces:**
- Produces: `internal static class FileLinks { uint LinkCount(string path); uint? ReparseTag(string path); const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C, IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003; }` (P/Invoke `GetFileInformationByHandle`, `FindFirstFileW`/`GetFileInformationByHandleEx`, opened with `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS`).

Rules, one commit each:
1. Files with link count > 1 are deleted, never re-ACL'd or moved (re-ACL would change a file outside the data dir).
2. Reparse points: delete only `SYMLINK` / `MOUNT_POINT`; other tags are treated as ordinary entries, never followed.
3. Links are removed **before** the root DACL is changed.
4. `Enforce` failures are logged to the service file log (Serilog) as well as the Event Log.
5. Loopback fallback: the running message and the `AuthApi` port self-probe use the fallback port actually bound (8735 over http), not the configured port — stop the 409 when probing ourselves.

- [ ] **Step 1: Failing tests** — `Hard_link_in_data_dir_is_deleted_and_target_acl_unchanged` (target file outside the dir; compare its SDDL before/after), `Junction_to_outside_dir_is_removed_target_untouched` (Review Focus 5: junction → a temp dir standing in for `C:\Windows`; assert junction gone, target SDDL unchanged), `Symlink_is_removed`, `LinkCount_and_ReparseTag_report_correctly` (FileLinksTests), `Fallback_running_message_names_bound_port` and `Port_probe_of_current_fallback_port_is_not_conflict` (NetworkConfigTests / Security tests).
- [ ] **Step 2:** Run the two test projects with `--filter` on these names → FAIL.
- [ ] **Step 3:** Implement rules 1–5 in order, committing after each passes.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5: Commits** `fix(acl): delete hard links instead of re-permissioning them`, `fix(acl): limit reparse deletion to symlinks and junctions`, `fix(acl): remove links before changing the root DACL`, `fix(service): log ACL enforcement failures to the file log`, `fix(service): report and probe the actual loopback fallback port`.

---

### Task 6: I5b — quarantine on takeover, crash-safe

**Files:**
- Modify: `src/Glance.Platform.Windows/DataDirectoryAcl.cs`, `src/Glance.Service/Program.cs`
- Test: `tests/Glance.Platform.Windows.Tests/DataDirectoryAclTests.cs`, `tests/Glance.Service.Tests/NoticesApiTests.cs`

**Interfaces:**
- Consumes: `AclResult` (Task 4), `FileLinks` (Task 5), `NoticeBoard` (Task 3).
- Produces: `public interface ITakeoverMarker { string? Read(); void Write(string quarantineDirName); void Clear(); }`; production `FileTakeoverMarker` (file `<parent of root>\Glance.takeover-pending`, i.e. `%ProgramData%\Glance.takeover-pending` for the default root); tests use an in-memory fake plus temp-dir tests of `FileTakeoverMarker`. `Enforce(string root, ITakeoverMarker marker)`. Nothing is written to the registry (owner's decision).
- Produces: quarantine folder `quarantine-<UTC yyyyMMddTHHmmssZ>` in the root.

**Marker placement (owner's decision: a file in ProgramData, no registry):** the marker sits next to the data dir, not inside it. A file in the root cannot be written before ownership is taken, so a crash between "take ownership" and "write marker" would leave planted files that the next start would merely reset (I5a) and trust. Writing the sibling file *first* closes that window.

`FileTakeoverMarker` rules (any user can create files in `%ProgramData%`, so the file must prove it came from us):
- `Write`: if a file already exists, delete it (Administrators/SYSTEM can always take ownership and delete); create it fresh with owner Administrators, DACL SYSTEM + Administrators full control only, inheritance off; content = quarantine folder name; write via temp file + rename in the same directory, then re-apply the ACL.
- `Read`: returns the content only when the file is owned by SYSTEM/Administrators, has no inherited ACEs and no ACE granting write/delete to anyone else, is not a reparse point, and the content matches `^quarantine-\d{8}T\d{6}Z$`. Otherwise the file is untrusted: delete it, log a warning, return null.
- `Clear`: delete the file; missing file is fine.
- A user cannot delete or edit the trusted file (its DACL denies them, and Users lack delete-child on `%ProgramData%`), so they can neither forge a resume nor cancel one.

Order (spec §6.3), when ownership must be taken (`OwnershipTaken` today) or the marker is set:
1. Write the marker (target quarantine folder name).
2. Take ownership of the root and harden its DACL (root only).
3. Move every other entry (except existing `quarantine-*` folders) into the quarantine folder (same volume; renames).
4. Harden the quarantine tree (owner Administrators, inherit from root, explicit ACEs dropped; links handled per Task 5).
5. Clear the marker. Result `Outcome.Quarantined`, `QuarantineDir` set.

On startup with the marker set: resume from step 2 using the folder named in the marker (each step is idempotent). Service then starts with defaults (fresh settings, keys, layouts; themes reseed via Task 11) and adds notice id `quarantine-<timestamp>`, severity `warning`, text: `Glance found files in its data folder it couldn't trust and moved them to <folder>. Settings were reset; set your password again if you use network access.` Log a warning with the folder path.

- [ ] **Step 1: Failing tests** — `Takeover_with_planted_settings_quarantines_everything` (user-owned root with `settings.json`, `keys/`, `https.pfx`, `layouts/x.json` → all under `quarantine-*`, root hardened, outcome `Quarantined`); `Crash_after_ownership_resumes_on_next_start` (root already hardened + fake marker set + planted files → `Enforce` quarantines them, marker cleared, outcome `Quarantined`); `Crash_mid_move_resumes_into_same_folder` (half the files already in the named quarantine folder → all end up there, one folder only); `Marker_is_written_before_ownership_changes` (fake marker records call order against a fake ACL step, or assert marker set when step 2 throws); `Existing_quarantine_folders_are_not_nested`; `File_marker_round_trips_with_hardened_acl`, `File_marker_owned_by_user_is_ignored_and_deleted`, `File_marker_with_bad_content_is_ignored`, `File_marker_that_is_a_symlink_is_ignored`, `Write_replaces_a_preplanted_user_file` (all elevated, temp parent dir); service test `Quarantine_raises_notice`.
- [ ] **Step 2:** Run `DataDirectoryAclTests` elevated → FAIL.
- [ ] **Step 3:** Implement; document the order in `Enforce`'s summary.
- [ ] **Step 4:** Run → PASS; `dotnet test` → PASS.
- [ ] **Step 5: Commit** `fix(acl): quarantine untrusted files on takeover, crash-safe (I5b)` and `feat(service): warn the user after a quarantine`.

---

### Task 7: Service address and CLI modes

**Files:**
- Create: `src/Glance.Service/ServiceAddress.cs`, `src/Glance.Service/Cli.cs`
- Modify: `src/Glance.Service/Program.cs` (dispatch `Cli.TryRun(args)` before building the host; return its exit code if handled)
- Test: `tests/Glance.Service.Tests/ServiceAddressTests.cs`, `tests/Glance.Service.Tests/CliTests.cs`

**Interfaces:**
- Produces: `public static Uri ServiceAddress.LocalUrl(AppSettings s, StartupBinding binding)` → `https://localhost:<port>/` when `Https` and binding is not fallback; `http://localhost:8735/` for `LoopbackFallback`; otherwise `http://localhost:<port>/`.
- Produces: `public static int? Cli.TryRun(string[] args)`:
  - `--prepare-data-dir` → `DataDirectoryAcl.CreateHardenedIfMissing(GlancePaths default root)`: if the directory does not exist, create it with `DataDirectoryAcl.Hardened()` and return true; if it exists, touch nothing and return false (the service's I5b handling owns existing dirs). Exit 0.
  - `--print-url` → reads `settings.json` (elevated installer can read it; missing → defaults), prints `LocalUrl(settings, Configured)`. Exit 0.
  - `--wait-ready <seconds>` → polls `GET <LocalUrl>api/health` every 500 ms; accepts the self-signed certificate only for `localhost`; exit 0 when 200, 1 on timeout.
  - Anything else → `null` (run the service).

- [ ] **Step 1: Failing tests** — `Https_uses_https_and_configured_port`, `Fallback_uses_http_8735_even_with_https_on` (Review Focus 3), `Plain_http_uses_port`; `CreateHardenedIfMissing_creates_hardened_dir` and `CreateHardenedIfMissing_leaves_existing_dir_untouched` (temp paths; elevated), `Unknown_args_return_null`, `Wait_ready_times_out_with_exit_1` (unused port, 1 s).
- [ ] **Step 2:** `dotnet test tests/Glance.Service.Tests --filter "FullyQualifiedName~ServiceAddressTests|FullyQualifiedName~CliTests"` → FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same → PASS.
- [ ] **Step 5: Commit** `feat(service): derive the local UI address in one place; add installer CLI modes`

---

### Task 8: Service-owned shortcuts

**Files:**
- Create: `src/Glance.Platform.Windows/UrlShortcuts.cs`, `src/Glance.Service/ShortcutSync.cs`
- Modify: `src/Glance.Service/Program.cs`
- Test: `tests/Glance.Platform.Windows.Tests/UrlShortcutsTests.cs`, `tests/Glance.Service.Tests/ShortcutSyncTests.cs`

**Interfaces:**
- Produces: `UrlShortcuts.RewriteExisting(IEnumerable<string> paths, Uri url)` — for each path that exists, atomically writes `[InternetShortcut]\r\nURL=<url>\r\n`; missing files are not created (a shortcut the user deleted stays deleted; the installer's checkbox choice is recorded by which files exist).
- Produces: `ShortcutSync` (hosted service): on start and on `SettingsStore.Changed` when `Port` or `Https` changed, calls `RewriteExisting` for `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Glance.url` and `%PUBLIC%\Desktop\Glance.url` with `ServiceAddress.LocalUrl` (Task 7) using the actual startup binding.
- Paths are injected (constructor) so tests use temp dirs.

- [ ] **Step 1: Failing tests** — `Rewrites_existing_shortcut_with_new_url`, `Does_not_create_missing_shortcut`; `Sync_rewrites_on_port_change`, `Sync_ignores_unrelated_setting_change`, `Sync_uses_fallback_address_when_fallback`.
- [ ] **Step 2:** Run filtered → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5: Commit** `feat(service): keep Start-menu and desktop shortcuts pointing at the current address`

---

### Task 9: Service-owned firewall rule

**Files:**
- Create: `src/Glance.Platform.Windows/IFirewall.cs`, `src/Glance.Platform.Windows/NetshFirewall.cs`, `src/Glance.Service/FirewallSync.cs`
- Modify: `src/Glance.Service/Program.cs`
- Test: `tests/Glance.Service.Tests/FirewallSyncTests.cs`

**Interfaces:**
- Produces: `public interface IFirewall { int? RulePort(); void Upsert(int port); void Remove(); }` and `const string RuleName = "Glance web UI"`.
- `NetshFirewall`: `netsh advfirewall firewall` with `name="Glance web UI" dir=in action=allow protocol=TCP localport=<port> program="<service exe>"`; `Upsert` deletes then adds (exactly one rule); `RulePort` parses `show rule name=…` (null if absent).
- `FirewallSync` (hosted service): desired = `NetworkAccess && binding is the configured network binding` ? port : none. Apply on start and on `SettingsStore.Changed` (`NetworkAccess` or `Port`). Failures are logged, never fatal. Skipped (logged once) when not elevated.

- [ ] **Step 1: Failing tests** (fake `IFirewall` recording calls) — `Network_on_upserts_port`, `Network_off_removes_rule`, `Port_change_moves_rule` (Review Focus 2: after change, single upsert with new port, fake holds one rule), `Fallback_binding_removes_rule`, `Firewall_failure_is_logged_not_thrown`.
- [ ] **Step 2:** Filtered run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS.
- [ ] **Step 5: Commit** `feat(service): own the firewall rule; it exists only while network access is on`

---

### Task 10: Embedded JetBrains Mono

**Files:**
- Add: `src/Glance.Core/Fonts/JetBrainsMono-Regular.ttf`, `src/Glance.Core/Fonts/JetBrainsMono-Bold.ttf` (official release, OFL-1.1)
- Modify: `src/Glance.Core/Rendering/FontProvider.cs`, `src/Glance.Service/Api/UiSupportApi.cs` (font list), `THIRD-PARTY-NOTICES.md`
- Test: `tests/Glance.Core.Tests/Rendering/FontProviderTests.cs` (create if absent)

**Interfaces:**
- Produces: family name `"JetBrains Mono"` resolves to the embedded face (weight ≥ 600 → Bold) regardless of what is installed; listed first-class in `GET /api/fonts` families.

- [ ] **Step 1: Failing test** `JetBrains_Mono_resolves_to_embedded_face` (typeface `FamilyName == "JetBrains Mono"`; `IsFixedPitch`), and `Fonts_api_lists_JetBrains_Mono`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement, add notices row (version, files, OFL-1.1, copyright line). **Step 4:** Run → PASS; `cd web && npm test` (notices test) → PASS.
- [ ] **Step 5: Commit** `feat(fonts): embed JetBrains Mono (OFL)`

---

### Task 11: Theme seeding and default layout

**Files:**
- Create: `src/Glance.Core/Layouts/ThemeSeeder.cs`
- Modify: `src/Glance.Core/Layouts/AppSettings.cs` (`List<string> SeededThemes`; `DeviceSettings.DefaultLayoutApplied` bool), `src/Glance.Core/Sessions/DeviceManager.cs`, `src/Glance.Service/Program.cs`, `src/Glance.Service/Glance.Service.csproj` (`themes/*.json` → `CopyToOutputDirectory`/publish)
- Test: `tests/Glance.Core.Tests/Layouts/ThemeSeederTests.cs`, `tests/Glance.Core.Tests/Sessions/DeviceManagerTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyList<string> ThemeSeeder.Seed(string themesDir, LayoutStore layouts, SettingsStore settings)` — returns names copied. For each `*.json` in `themesDir` whose layout name is not in `SeededThemes`: if no layout of that name exists, save it; add the name to `SeededThemes` either way. Never overwrites. Runs at startup before `DeviceManager` starts. `themesDir` = `Path.Combine(AppContext.BaseDirectory, "themes")`.
- Produces: `DeviceManager` gains a `Func<string, bool> layoutExists` constructor parameter (`Program.cs` passes `n => layouts.Get(n) is not null`). In `StartSession`, inside the same `Update`: if `!d.DefaultLayoutApplied` then `if (d.ActiveLayout is null && layoutExists("Blend")) d.ActiveLayout = "Blend"; d.DefaultLayoutApplied = true;`.

- [ ] **Step 1: Failing tests** — `Seeds_missing_themes_and_records_them`, `Deleted_theme_is_not_reseeded`, `Existing_layout_with_theme_name_is_not_overwritten` (Review Focus 1: pre-existing user `Blend` content unchanged, name recorded), `Reseeds_after_settings_reset` (empty `SeededThemes`, layout absent → copied); DeviceManager: `New_device_gets_Blend`, `Existing_device_with_null_layout_gets_Blend_once`, `Cleared_layout_stays_cleared` (after applied, set null, reconnect → still null), `Device_with_layout_keeps_it`, `Missing_Blend_leaves_null_and_marks_applied`.
- [ ] **Step 2:** `dotnet test tests/Glance.Core.Tests --filter "FullyQualifiedName~ThemeSeederTests|FullyQualifiedName~DeviceManagerTests"` → FAIL.
- [ ] **Step 3:** Implement; also confirm `SettingsRepair` (Task 2) handles the new fields (add one test: bad `seededThemes` type resets to empty).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5: Commit** `feat(themes): seed shipped themes once and give new screens the Blend layout`

---

### Task 12: The four themes

**Files:**
- Create: `src/Glance.Service/themes/Blend.json`, `Clean.json`, `Gauges.json`, `Live graphs.json`
- Test: `tests/Glance.Core.Tests/Layouts/ShippedThemesTests.cs`, golden PNGs `tests/Glance.Core.Tests/Golden/theme-<name>.png`

Content per spec §4.1–§4.2 (all show the same data): CPU temp / package power / clock / load; GPU temp / core clock / power / VRAM used (GB) with total or VRAM %; RAM used and available (GB); date and time (respecting `clock24h`). Uses only existing item types (`arcGauge`, `sparkline`, `historyGraph`, `bar`/`segmentedBar`, `sensor`, `clock`, `text`). Sensor bindings use roles (`cpu.temperature`, …). Backgrounds: colour or gradient only. Fonts: Inter, plus JetBrains Mono in Live graphs. Look reference: `.superpowers/brainstorm/914-1792826911/content/theme-styles.html` and `hybrid.html` (Blend = option 1).

- [ ] **Step 1: Failing tests** — `[Theory] Every_shipped_theme_is_valid(name)`: loads, `LayoutValidator` returns no errors, size 1280×480, every sensor binding is a role in `Roles.All`; `Every_shipped_theme_covers_required_roles` (cpu.temperature, cpu.power.package, cpu.clock, cpu.load, gpu.temperature, gpu.clock, gpu.power, gpu.memory.used, memory.used, memory.available, plus a clock item); `Theme_renders_matches_golden(name)` with `FakeSensorSource` fixed values.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Author each theme; generate goldens with the existing golden-update mechanism; inspect each PNG by eye.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5: Commits** one per theme: `feat(themes): add Blend`, `… Clean`, `… Gauges`, `… Live graphs` (validity/coverage test in the first commit).

---

### Task 13: Inno Setup installer — guided

**Files:**
- Create: `installer/glance.iss`, `installer/build.ps1`
- Modify: `README.md` (build-from-source section)

**Interfaces:**
- Consumes: `Glance.Service.exe --prepare-data-dir | --print-url | --wait-ready 30` (Task 7); themes in publish output (Task 11).
- `build.ps1 -Version <x.y.z>`: `dotnet publish src/Glance.Service -c Release -r win-x64 --self-contained -p:Version=<v> -o out/publish`, then `iscc /DAppVersion=<v> /DSourceDir=out\publish installer\glance.iss` → `out/Glance-Setup-<v>.exe`. Default version `0.0.0-dev`.

Script requirements (spec §3.1, §3.4):
- `AppId` fixed GUID (generate once, never change); `PrivilegesRequired=admin`; `ArchitecturesInstallIn64BitMode=x64compatible`; `DefaultDirName={autopf}\Glance`; `LicenseFile=..\LICENSE`; `OutputBaseFilename=Glance-Setup-{#AppVersion}`.
- Tasks: `startmenu` (checked), `desktopicon` (unchecked), `pawnio` (checked; added in Task 14). "Open Glance when setup finishes" = `[Run]` `postinstall skipifsilent` entry opening `--print-url` output.
- Install order in `CurStepChanged(ssPostInstall)`: stop service if present (upgrade) happens in `PrepareToInstall`; after files: run `--prepare-data-dir`; `sc create Glance binPath= "<app>\Glance.Service.exe" start= auto obj= LocalSystem` (or `sc config` on upgrade); `sc start Glance`; run `--wait-ready 30`; on non-zero show "Glance installed but did not answer within 30 s. See %ProgramData%\Glance\logs." (no rollback).
- Shortcuts are `.url` files written by the installer from `--print-url` output at the exact paths in Task 8.

- [ ] **Step 1:** Write the script and `build.ps1`; run `./installer/build.ps1` → `out/Glance-Setup-0.0.0-dev.exe` exists, `iscc` exit 0.
- [ ] **Step 2:** In a VM or on the dev machine (owner present): run it guided; verify service `Glance` running as LocalSystem, data dir ACL hardened (`icacls`), browser opens `--print-url` address, Start-menu shortcut opens it.
- [ ] **Step 3: Commit** `feat(installer): guided Inno Setup installer`

---

### Task 14: Installer — silent mode, PawnIO, TRCC, uninstall

**Files:**
- Modify: `installer/glance.iss`, `README.md` (switches)

Requirements:
- **Silent** (spec §3.2): `/VERYSILENT` and `/SILENT` use the default task set and never open a browser; `/TASKS="!pawnio"` skips PawnIO; `--wait-ready` failure → `ExitCode` non-zero via `SetupSetExitCode`-style handling (abort with error in silent mode).
- **PawnIO** (spec §3.3): `#define PawnIoUrl` and `#define PawnIoSha256` for a specific release of the official PawnIO setup (look up the latest release, record version in a comment). Skip task page entry (or show "already installed") when service `PawnIO` exists. Download via `CreateDownloadPage`/`DownloadTemporaryFile` with the SHA-256 argument (Inno verifies; mismatch = no run). Run its silent install switch (verify the switch against PawnIO's docs). Download/install failure: continue; finish page and log say CPU and motherboard sensors are unavailable until PawnIO is installed, with the PawnIO link.
- **TRCC** (spec §3.1 step 4): detect TRCC's process (name per parent design §9.2; confirm on hardware). Guided: page asking to close it and offering to remove its logon task (task name from parent design §9.2). Silent: do not kill; `Log()` and continue.
- **Uninstall** (spec §3.5): stop + delete service; `netsh advfirewall firewall delete rule name="Glance web UI"`; delete both `.url` shortcuts; guided: ask whether to delete `%ProgramData%\Glance`; silent: keep it. Never remove PawnIO.

- [ ] **Step 1:** Implement silent + README switch table; build; run `Glance-Setup-0.0.0-dev.exe /VERYSILENT /SUPPRESSMSGBOXES /LOG=out\setup.log` → exit 0, service running, no browser. Commit `feat(installer): silent mode`.
- [ ] **Step 2:** PawnIO download with pinned hash; test with a deliberately wrong hash build → install continues, log says sensors unavailable. Commit `feat(installer): download PawnIO at install time`.
- [ ] **Step 3:** TRCC page/silent logging. Commit `feat(installer): handle a running TRCC`.
- [ ] **Step 4:** Uninstall (guided + `/VERYSILENT`): service, rule, shortcuts gone; data dir kept on silent. Commit `feat(installer): clean uninstall`.

---

### Task 15: GitHub release workflow

**Files:**
- Create: `.github/workflows/release.yml`, `installer/release-notes.md`
- Modify: `Directory.Build.props` (`<Version Condition="'$(Version)' == ''">0.0.0-dev</Version>`), `README.md` (release process: owner pushes `vX.Y.Z`)

Workflow (spec §8a):
- `on: push: tags: ['v*']` and `workflow_dispatch`. `permissions: contents: write`. `runs-on: windows-latest`.
- Steps: checkout (`fetch-depth: 0` for notes) → `actions/setup-dotnet` 8.0.x → `actions/setup-node` (web build) → `dotnet test` (all projects; fails the run) → version = tag without `v`, or `0.0.0-dev` on dispatch → install Inno Setup if `iscc` missing (`choco install innosetup -y`) → `installer/build.ps1 -Version $v` → `SHA256SUMS.txt` (`Get-FileHash`, format `<hash>  <file>`) → on tag: `gh release create $tag out/Glance-Setup-$v.exe out/SHA256SUMS.txt --title "Glance $v" --notes-file <rendered notes>` with `--prerelease` when the tag contains `-`; on dispatch: `actions/upload-artifact` of both files.
- Notes rendering: `release-notes.md` template with `{{VERSION}}`, `{{SHA256}}`, `{{CHANGES}}` (`git log --oneline <previous tag>..HEAD`, or all commits if no previous tag); template includes SmartScreen "More info → Run anyway" steps and the silent switches.
- Tests that need elevation or hardware stay skipped on CI by their existing skip conditions; check none fails on a non-elevated runner.

- [ ] **Step 1:** Write workflow + template; lint with `actionlint` if available.
- [ ] **Step 2:** Locally: `./installer/build.ps1 -Version 0.3.0-test` → installer reports version 0.3.0-test in Programs and Features and `GET /api/health` version.
- [ ] **Step 3: Commit** `ci: build installer and publish GitHub releases from version tags`
- [ ] **Step 4:** Ask the owner to push the branch and run the workflow manually (dispatch); download the artifact.

---

### Task 16: Hardware smoke and first release (owner)

- [ ] Install the CI-built artifact on the owner's machine (LCD `0416:5302`) over the existing Plan 2 install: screen shows Blend within a minute (or keeps the owner's active layout), UI opens at the right address, all four themes render on the real screen and the owner approves them.
- [ ] Elevated ACL test on the real machine: planted user-owned file under hardened dir → reset; planted takeover → quarantine + banner.
- [ ] Firewall: toggle network access → rule appears/disappears (`netsh advfirewall firewall show rule name="Glance web UI"`); change port → rule follows.
- [ ] Silent install + silent uninstall on a clean VM.
- [ ] Record results in `docs/testing/hardware-smoke.md`; commit `docs: record Plan 3 hardware smoke`.
- [ ] Owner merges, tags `v0.3.0`, pushes the tag; release appears with installer and checksums.

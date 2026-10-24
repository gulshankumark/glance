## Glance {{VERSION}}

Download `Glance-Setup-{{VERSION}}.exe`, then run it as an administrator.

SHA-256 of the installer: `{{SHA256}}` (also in `SHA256SUMS.txt`; verify with `sha256sum -c SHA256SUMS.txt`).

### SmartScreen

The installer is not code-signed, so Windows SmartScreen may show "Windows protected your PC".
Click **More info → Run anyway**.

### PawnIO

PawnIO (the driver used for sensor access) is downloaded at install time, so the installer needs internet access
unless you skip it with `/MERGETASKS="!pawnio"`.

### Silent install

```
Glance-Setup-{{VERSION}}.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=C:\Temp\glance-setup.log
```

| Switch | Effect |
|---|---|
| `/VERYSILENT` | No wizard and no progress window. |
| `/SUPPRESSMSGBOXES` | Answers message boxes with their default. |
| `/NORESTART` | Never restart Windows. |
| `/LOG=<file>` | Writes the setup log to `<file>`. |
| `/MERGETASKS="!pawnio"` | Skips the PawnIO download and install. |
| `/DIR=<folder>` | Install folder. Must be inside `C:\Program Files`, otherwise setup stops with a non-zero exit code. |

Exit code `10` means the files were installed but the service did not answer.

### Changes

{{CHANGES}}

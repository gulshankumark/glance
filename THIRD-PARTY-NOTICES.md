# Third-party notices

Glance is licensed under GPL-3.0. Each NuGet dependency added to the project is listed here with its license.

| Package | Used by | License |
|---|---|---|
| xunit, xunit.runner.visualstudio | test projects | Apache-2.0 |
| Microsoft.NET.Test.Sdk | test projects | MIT |
| HidSharp 2.1.0 | Glance.Core (HID access) | Apache-2.0 |
| Microsoft.Extensions.Logging.Abstractions 8.0.2 | Glance.Core (ILogger) | MIT |
| Microsoft.Extensions.Hosting.Abstractions 8.0.1 | Glance.Core (BackgroundService) | MIT |
| LibreHardwareMonitorLib 0.9.6 | Glance.Sensors.Windows (hardware sensors) | MPL-2.0 |
| System.ServiceProcess.ServiceController 8.0.1 | Glance.Sensors.Windows (PawnIO service control) | MIT |
| Microsoft.Extensions.TimeProvider.Testing 8.10.0 | Glance.Core.Tests, Glance.Testing (FakeTimeProvider) | MIT |
| SkiaSharp 3.119.4 | Glance.Core (rendering, JPEG) | MIT |
| Serilog.Extensions.Hosting 8.0.0 | Glance.Service (logging) | Apache-2.0 |
| Serilog.Sinks.File 6.0.0 | Glance.Service (rolling log files) | Apache-2.0 |
| Microsoft.Extensions.Hosting.WindowsServices 8.0.1 | Glance.Service (Windows service host) | MIT |
| Microsoft.AspNetCore.Mvc.Testing 8.0.11 | Glance.Service.Tests | MIT |
| Inter 4.1 (Inter-Regular.ttf, Inter-Bold.ttf, embedded font resources) | Glance.Core (default font) | SIL OFL-1.1 |
| JetBrains Mono 2.304 (JetBrainsMono-Regular.ttf, JetBrainsMono-Bold.ttf, embedded font resources) | Glance.Core (monospace font) | SIL OFL-1.1 |

Inter font: Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter), licensed under the SIL Open Font License 1.1 (https://openfontlicense.org).

JetBrains Mono font: Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono), licensed under the SIL Open Font License 1.1 (https://openfontlicense.org).

## Web UI npm packages

Versions are the ones locked in `web/package-lock.json`. Node.js and npm are needed only to build the UI; nothing
from npm runs on the service.

### Shipped in the UI bundle (`src/Glance.Service/wwwroot`)

| Package | Used by | License |
|---|---|---|
| react 18.3.1 | web UI runtime (bundled) | MIT |
| react-dom 18.3.1 | web UI runtime (bundled) | MIT |
| scheduler 0.23.2 | dependency of react-dom (bundled) | MIT |
| loose-envify 1.4.0 | dependency of react / react-dom (install-time build transform, not in the bundle) | MIT |
| js-tokens 4.0.0 | dependency of loose-envify (not in the bundle) | MIT |

The bundle also contains Vite's small module-preload helper (vite, MIT, listed below).

### Build and test tooling (devDependencies, not shipped)

| Package | Used by | License |
|---|---|---|
| vite 5.4.21 | web UI build / bundler | MIT |
| @vitejs/plugin-react 4.7.0 | web UI build | MIT |
| typescript 5.9.3 | web UI typechecker | Apache-2.0 |
| @types/react 18.3.31 | web UI types | MIT |
| @types/react-dom 18.3.7 | web UI types | MIT |
| @types/node 20.19.43 | web UI test types | MIT |
| vitest 2.1.9 | web UI unit tests | MIT |
| @testing-library/react 16.3.3 | web UI component tests | MIT |
| @testing-library/dom 10.4.2 | web UI component tests | MIT |
| jsdom 26.1.0 | web UI test DOM environment | MIT |
| @playwright/test 1.64.1 | web UI e2e browser tests | Apache-2.0 |

Dev-only tooling pulls in further packages transitively (about 180, all marked `dev` in `web/package-lock.json`). Their
licence fields are MIT, ISC, Apache-2.0, BSD-2-Clause, BSD-3-Clause, MIT-0 (@csstools/color-helpers, via jsdom) and
CC-BY-4.0 (caniuse-lite, browser-support data used by Babel via @vitejs/plugin-react). None of them is shipped in
the UI bundle.

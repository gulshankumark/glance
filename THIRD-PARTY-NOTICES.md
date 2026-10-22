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

Inter font: Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter), licensed under the SIL Open Font License 1.1 (https://openfontlicense.org).

## Web UI npm packages

| Package | Used by | License |
|---|---|---|
| react 18.3.1 | web UI runtime | MIT |
| react-dom 18.3.1 | web UI runtime | MIT |
| @types/react 18.3.18 | web UI types | MIT |
| @types/react-dom 18.3.5 | web UI types | MIT |
| @vitejs/plugin-react 4.3.4 | web UI build | MIT |
| vite 5.4.14 | web UI build / bundler | MIT |
| typescript 5.7.3 | web UI typechecker | Apache-2.0 |
| vitest 2.1.8 | web UI unit tests | MIT |
| @testing-library/react 16.2.0 | web UI component tests | MIT |
| @testing-library/dom 10.4.0 | web UI component tests | MIT |
| jsdom 26.0.0 | web UI test DOM environment | MIT |
| @playwright/test 1.50.1 | web UI e2e browser tests | Apache-2.0 |
| @types/node 22.13.1 | web UI test types | MIT |

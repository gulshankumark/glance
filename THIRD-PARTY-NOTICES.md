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

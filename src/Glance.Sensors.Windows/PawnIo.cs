using System.ServiceProcess;

namespace Glance.Sensors.Windows;

public enum PawnIoStatus { NotInstalled, Stopped, Running }

/// <summary>Inspects and starts the PawnIO driver service that LibreHardwareMonitor needs for CPU and motherboard sensors.</summary>
public class PawnIo
{
    public const string ServiceName = "PawnIO";

    public virtual PawnIoStatus GetStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status == ServiceControllerStatus.Running ? PawnIoStatus.Running : PawnIoStatus.Stopped;
        }
        catch (InvalidOperationException)
        {
            return PawnIoStatus.NotInstalled;
        }
    }

    /// <summary>Tries to start the service; false when it cannot (not elevated, not installed, timeout).</summary>
    public virtual bool TryStart()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }
}

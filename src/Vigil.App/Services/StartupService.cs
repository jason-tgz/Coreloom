using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Vigil.Core.Diagnostics;
using Vigil.ViewModels.Services;

namespace Vigil.App.Services;

/// <summary>
/// Start-at-sign-in support. Normal startup uses the per-user Run key (no admin needed). "Start with
/// administrator rights" creates a Task Scheduler logon task with highest privileges — the supported way to
/// launch an elevated app at sign-in without a UAC prompt each time. Creating/removing that task shows one
/// UAC prompt.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Vigil";
    public const string TaskName = "Vigil (elevated startup)";

    public bool IsRunAtLogonEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetRunAtLogon(bool enabled, bool minimized)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{LifetimeService.ExecutablePath}\" --startup{(minimized ? " --minimized" : string.Empty)}", RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Warn("Startup", "Updating the Run key failed.", ex);
        }
    }

    public bool IsElevatedTaskRegistered
    {
        get
        {
            try
            {
                var info = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(info);
                if (process is null)
                {
                    return false;
                }

                process.WaitForExit(5000);
                return process.HasExited && process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool> SetElevatedTaskAsync(bool enabled, bool minimized)
    {
        string arguments;
        string? xmlPath = null;
        if (enabled)
        {
            xmlPath = Path.Combine(Path.GetTempPath(), $"vigil-task-{Guid.NewGuid():N}.xml");
            await File.WriteAllTextAsync(xmlPath, BuildTaskXml(minimized), Encoding.Unicode);
            arguments = $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F";
        }
        else
        {
            if (!await Task.Run(() => IsElevatedTaskRegistered))
            {
                return true;
            }

            arguments = $"/Delete /TN \"{TaskName}\" /F";
        }

        try
        {
            var info = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = true,
                Verb = Environment.IsPrivilegedProcess ? string.Empty : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            bool ok = process.ExitCode == 0;
            if (!ok)
            {
                Log.Warn("Startup", $"schtasks exited with code {process.ExitCode}.");
            }

            return ok;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // UAC prompt cancelled
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "Updating the elevated startup task failed.", ex);
            return false;
        }
        finally
        {
            if (xmlPath is not null)
            {
                try
                {
                    File.Delete(xmlPath);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static string BuildTaskXml(bool minimized)
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string exe = SecurityElement.Escape(LifetimeService.ExecutablePath);
        string args = SecurityElement.Escape("--startup" + (minimized ? " --minimized" : string.Empty));
        string workDir = SecurityElement.Escape(AppContext.BaseDirectory);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts Vigil with administrator rights at sign-in so CPU temperature and motherboard sensors are available.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{exe}"</Command>
                  <Arguments>{args}</Arguments>
                  <WorkingDirectory>{workDir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}

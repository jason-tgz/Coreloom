using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Coreloom.Core.Diagnostics;
using Coreloom.ViewModels.Services;

namespace Coreloom.App.Services;

/// <summary>
/// Start-at-sign-in support. The app always runs as administrator, so "Start with Windows" is a Task Scheduler
/// logon task with the highest available privileges: that is the supported way to start an elevated app at sign-in
/// without a UAC prompt. The app is already elevated when it changes the task, so no extra prompt is involved.
/// Nothing is written to the Run key (a Run entry would trigger a UAC prompt at every sign-in for an app that
/// requires administrator rights); old Run entries from earlier versions are removed.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string TaskName = "Coreloom startup";

    /// <summary>Names used by earlier builds; removed when found.</summary>
    private static readonly string[] LegacyRunValues = ["Vigil"]; // legacy-name

    private static readonly string[] LegacyTaskNames = ["Vigil (elevated startup)"]; // legacy-name

    public bool IsStartAtLogonRegistered => TaskExists(TaskName);

    public async Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized)
    {
        RemoveLegacyRunValues();
        if (!enabled)
        {
            if (!await Task.Run(() => TaskExists(TaskName)))
            {
                return true;
            }

            return await RunSchtasksAsync($"/Delete /TN \"{TaskName}\" /F") == 0;
        }

        string xmlPath = Path.Combine(Path.GetTempPath(), $"coreloom-task-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(xmlPath, BuildTaskXml(minimized), Encoding.Unicode);
            return await RunSchtasksAsync($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Startup", "Could not write the startup task definition.", ex);
            return false;
        }
        finally
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

    /// <summary>
    /// Removes the Run-key value and the Task Scheduler task that earlier versions registered (they point at the old
    /// executable). Safe to call repeatedly; best effort.
    /// </summary>
    public async Task RemoveLegacyEntriesAsync()
    {
        RemoveLegacyRunValues();
        foreach (string name in LegacyTaskNames)
        {
            if (await Task.Run(() => TaskExists(name)))
            {
                await RunSchtasksAsync($"/Delete /TN \"{name}\" /F");
            }
        }
    }

    private static void RemoveLegacyRunValues()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            foreach (string name in LegacyRunValues)
            {
                if (key.GetValue(name) is not null)
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Warn("Startup", "Removing the old Run entry failed.", ex);
        }
    }

    private static bool TaskExists(string name)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{name}\"")
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

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunSchtasksAsync(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info);
            if (process is null)
            {
                return -1;
            }

            string error = await process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Log.Warn("Startup", $"schtasks {arguments.Split(' ')[0]} exited with code {process.ExitCode}: {error.Trim()}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "Updating the startup task failed.", ex);
            return -1;
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
                <Description>Starts Coreloom with administrator rights at sign-in, without a UAC prompt, so CPU temperature and motherboard sensors are available.</Description>
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

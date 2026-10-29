using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using QuickTranslate.Platform.Native;

namespace QuickTranslate.Platform;

/// <summary>Encrypts secrets (API keys) with DPAPI so they only open for this Windows user.</summary>
public static class Secrets
{
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>
/// A highest-privilege scheduled task lets the app start elevated (at logon or from a shortcut)
/// without a UAC prompt each time. Creating it needs one elevation, which the installer/app has.
/// </summary>
public static class LaunchTask
{
    public const string Name = "QuickTranslate";

    public static bool Exists() => Run("/Query /TN " + Name).ExitCode == 0;

    public static (bool Ok, string Output) Create(string exePath, bool runAtLogon, int delaySeconds)
    {
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>{(runAtLogon ? "true" : "false")}</Enabled>
                  <UserId>{System.Security.SecurityElement.Escape(user)}</UserId>
                  <Delay>PT{Math.Max(0, delaySeconds)}S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{System.Security.SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{System.Security.SecurityElement.Escape(exePath)}</Command>
                  <Arguments>--from-task</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "qt-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            var r = Run($"/Create /TN {Name} /XML \"{file}\" /F");
            return (r.ExitCode == 0, r.Output);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static (bool Ok, string Output) Delete()
    {
        var r = Run("/Delete /TN " + Name + " /F");
        return (r.ExitCode == 0, r.Output);
    }

    private static (int ExitCode, string Output) Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode, o);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}

public static class Elevation
{
    public static bool IsAdministrator()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}

/// <summary>Copies the current selection of whichever app has focus, then restores the clipboard.</summary>
public static class SelectionGrabber
{
    public static void SendCtrlC()
    {
        // Release hotkey modifiers first so the target app sees a clean Ctrl+C.
        foreach (var vk in new byte[] { 0x12, 0x10, 0x5B }) NativeMethods.keybd_event(vk, 0, 2, UIntPtr.Zero);
        NativeMethods.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(0x43, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(0x43, 0, 2, UIntPtr.Zero);
        NativeMethods.keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }

    public static void SendCtrlV()
    {
        foreach (var vk in new byte[] { 0x12, 0x10, 0x5B }) NativeMethods.keybd_event(vk, 0, 2, UIntPtr.Zero);
        NativeMethods.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(0x56, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(0x56, 0, 2, UIntPtr.Zero);
        NativeMethods.keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }

    public static string? ForegroundProcessName()
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var pid);
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return null; }
    }
}


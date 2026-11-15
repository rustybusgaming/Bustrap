using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Bustrap;

public static class RobloxMemoryCleaner
{
    #region Native

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(
        IntPtr hProcess,
        IntPtr dwMinimumWorkingSetSize,
        IntPtr dwMaximumWorkingSetSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(
        IntPtr ProcessHandle,
        uint DesiredAccess,
        out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(
        string lpSystemName,
        string lpName,
        out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;
    private const uint SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    #endregion

    private static readonly string[] RobloxProcesses =
    {
        "RobloxPlayerBeta",
        "RobloxPlayer",
        "Roblox",
        "RobloxStudioBeta"
    };

    private static bool _debugPrivilegeRequested;

    public static void EnableDebugPrivilege()
    {
        // once per process is enough, and each call used to leak a token handle
        if (_debugPrivilegeRequested)
            return;

        _debugPrivilegeRequested = true;

        using var self = Process.GetCurrentProcess();

        if (!OpenProcessToken(self.Handle,
            TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
            out IntPtr token))
            return;

        try
        {
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                return;

            TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static void CleanAllRobloxMemory()
    {
        EnableDebugPrivilege();

        foreach (var proc in Utilities.GetProcessesSafe())
        {
            try
            {
                if (!RobloxProcesses.Contains(proc.ProcessName))
                    continue;

                long before = proc.WorkingSet64;

                // Trimming memory is all this does. It used to drop the process
                // to below-normal priority as well, which slowed the game down
                // and overrode the priority chosen in settings on every pass.
                EmptyWorkingSet(proc.Handle);
                SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));

                proc.Refresh();
                long after = proc.WorkingSet64;

                App.Logger.WriteLine("RobloxMemoryCleaner",
                    $"[{proc.ProcessName}:{proc.Id}] {FormatBytes(before)} -> {FormatBytes(after)}");
            }
            catch (Exception ex)
            {
                // A process can exit mid-pass, or refuse to hand out a handle.
                // That used to abort the pass for every process after it.
                App.Logger.WriteLine("RobloxMemoryCleaner", $"Skipped a process: {ex.Message}");
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1073741824) return $"{bytes / 1048576.0:F1} MB";
        return $"{bytes / 1073741824.0:F2} GB";
    }
}
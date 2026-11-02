using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// Отдельный консольный exe (tkguard.exe), НЕ часть Toolkit.exe: собирается build.ps1 отдельно.
// Подключается как Debugger в Image File Execution Options для taskkill.exe.
// Windows запускает его вместо taskkill: «tkguard.exe <исходная командная строка taskkill>».
//
// Логика: если taskkill просит завершить explorer и вызван (прямо или через предков)
// скриптом GameFix — ничего не делаем и возвращаем 0. Во всех остальных случаях,
// включая любую ошибку проверки, запускается настоящий taskkill (копия tk-taskkill.exe рядом).
internal static class TaskkillGuard
{
    private const string BlockedCaller = "GameFix";
    private static readonly string DefaultTarget = @"\bexplorer(\.exe)?\b";

    private static int Main()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string real = Path.Combine(dir, "tk-taskkill.exe");
        string args = "";

        try
        {
            args = StripLeadingTokens(Environment.CommandLine, 2);

            string pattern = Environment.GetEnvironmentVariable("TKGUARD_PATTERN");
            if (string.IsNullOrEmpty(pattern)) pattern = DefaultTarget;

            if (Regex.IsMatch(args, pattern, RegexOptions.IgnoreCase))
            {
                string culprit = FindCaller(Process.GetCurrentProcess().Id, BlockedCaller);
                if (culprit != null)
                {
                    Log(dir, "BLOCKED args=[" + args + "] caller=[" + culprit + "]");
                    return 0;
                }

                Log(dir, "allowed args=[" + args + "]");
            }
        }
        catch (Exception ex)
        {
            Log(dir, "check failed, passing through: " + ex.Message);
        }

        return RunReal(real, args);
    }

    private static int RunReal(string real, string args)
    {
        if (!File.Exists(real)) return 1;

        ProcessStartInfo psi = new ProcessStartInfo(real, args);
        psi.UseShellExecute = false;
        using (Process p = Process.Start(psi))
        {
            p.WaitForExit();
            return p.ExitCode;
        }
    }

    /// <summary>Возвращает командную строку первого предка, содержащую needle, либо null.</summary>
    private static string FindCaller(int selfPid, string needle)
    {
        int pid = ParentOf(selfPid);
        for (int depth = 0; depth < 8 && pid > 0; depth++)
        {
            string cmd = CommandLineOf(pid);
            if (cmd != null && cmd.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return cmd;
            pid = ParentOf(pid);
        }
        return null;
    }

    private static string CommandLineOf(int pid)
    {
        using (ManagementObjectSearcher s = new ManagementObjectSearcher(
            "SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
        {
            foreach (ManagementObject mo in s.Get())
            {
                return mo["CommandLine"] as string;
            }
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private static int ParentOf(int pid)
    {
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return 0;

        try
        {
            PROCESSENTRY32 e = new PROCESSENTRY32();
            e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
            if (!Process32First(snap, ref e)) return 0;
            do
            {
                if (e.th32ProcessID == (uint)pid) return (int)e.th32ParentProcessID;
            }
            while (Process32Next(snap, ref e));
        }
        finally
        {
            CloseHandle(snap);
        }
        return 0;
    }

    /// <summary>Отбрасывает count первых токенов командной строки (с учётом кавычек), возвращает остаток.</summary>
    private static string StripLeadingTokens(string line, int count)
    {
        string s = line;
        for (int i = 0; i < count; i++)
        {
            s = s.TrimStart();
            if (s.Length == 0) return "";

            int end;
            if (s[0] == '"')
            {
                end = s.IndexOf('"', 1);
                end = end < 0 ? s.Length : end + 1;
            }
            else
            {
                end = s.IndexOf(' ');
                if (end < 0) end = s.Length;
            }
            s = s.Substring(end);
        }
        return s.Trim();
    }

    private static void Log(string dir, string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(dir, "guard.log"),
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}

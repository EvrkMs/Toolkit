using System;
using System.IO;
using Microsoft.Win32;

namespace Toolkit
{
    /// <summary>
    /// Ставит/снимает перехват taskkill.exe (IFEO Debugger -> tkguard.exe), который не даёт
    /// GameFix.bat убить explorer.exe. Любые другие вызовы taskkill проходят без изменений.
    /// Рабочие файлы лежат локально в %ProgramData%\ToolkitGuard, чтобы taskkill не зависел от диска E:.
    /// </summary>
    internal static class GuardVerb
    {
        private const string C = "GUARD";

        private const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskkill.exe";
        private const string IfeoKeyWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskkill.exe";

        private static string LocalDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ToolkitGuard"); }
        }

        public static int Run(ToolkitConfig cfg, Cli cli)
        {
            string action = (cli.SubVerb ?? "").ToLowerInvariant();
            switch (action)
            {
                case "install": return Install();
                case "remove": return Remove();
                case "status": return Status();
                case "":
                    return cfg.Guard.Enabled ? Install() : Remove();
                default:
                    Log.Write(C, "usage: Toolkit.exe guard install|remove|status");
                    return 2;
            }
        }

        /// <summary>Вызывается в начале run: идемпотентно, при выключенном guard снимает перехват.</summary>
        public static int Ensure(ToolkitConfig cfg)
        {
            return cfg.Guard.Enabled ? Install() : Remove();
        }

        public static int Install()
        {
            try
            {
                string source = Path.Combine(Paths.BaseDir, "tkguard.exe");
                if (!File.Exists(source))
                {
                    Log.Write(C, "FAIL tkguard.exe not found next to Toolkit.exe: " + source);
                    return 10;
                }

                Directory.CreateDirectory(LocalDir);
                string guardExe = Path.Combine(LocalDir, "tkguard.exe");

                CopyIfChanged(source, guardExe);
                CopyRealTaskkill();

                if (!File.Exists(guardExe) || !File.Exists(Path.Combine(LocalDir, "tk-taskkill.exe")))
                {
                    Log.Write(C, "FAIL local files missing, hook not installed");
                    return 10;
                }

                string value = "\"" + guardExe + "\"";
                bool changed = SetDebugger(IfeoKey, value) | SetDebugger(IfeoKeyWow, value);
                Log.Write(C, changed ? "installed taskkill hook -> " + guardExe : "hook already installed");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write(C, "FAIL install: " + ex.Message);
                return 10;
            }
        }

        public static int Remove()
        {
            try
            {
                bool changed = ClearDebugger(IfeoKey) | ClearDebugger(IfeoKeyWow);
                if (changed) Log.Write(C, "taskkill hook removed");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write(C, "FAIL remove: " + ex.Message);
                return 10;
            }
        }

        private static int Status()
        {
            string v1 = GetDebugger(IfeoKey);
            string v2 = GetDebugger(IfeoKeyWow);
            Log.Write(C, "status: x64=" + (v1 ?? "-") + " wow64=" + (v2 ?? "-") +
                         " files=" + File.Exists(Path.Combine(LocalDir, "tkguard.exe")) + "/" +
                         File.Exists(Path.Combine(LocalDir, "tk-taskkill.exe")));
            return 0;
        }

        /// <summary>Копия настоящего taskkill под другим именем (IFEO привязан к имени образа) вместе с MUI-ресурсами.</summary>
        private static void CopyRealTaskkill()
        {
            string sys = Environment.SystemDirectory;
            CopyIfChanged(Path.Combine(sys, "taskkill.exe"), Path.Combine(LocalDir, "tk-taskkill.exe"));

            foreach (string langDir in Directory.GetDirectories(sys))
            {
                string mui = Path.Combine(langDir, "taskkill.exe.mui");
                if (!File.Exists(mui)) continue;

                string target = Path.Combine(LocalDir, Path.GetFileName(langDir));
                Directory.CreateDirectory(target);
                CopyIfChanged(mui, Path.Combine(target, "tk-taskkill.exe.mui"));
            }
        }

        private static void CopyIfChanged(string source, string target)
        {
            try
            {
                if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length) return;
                File.Copy(source, target, true);
            }
            catch (IOException)
            {
                // Файл занят (tkguard.exe сейчас выполняется): остаётся прежняя копия.
            }
        }

        private static string GetDebugger(string subKey)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, false))
            {
                return key == null ? null : key.GetValue("Debugger") as string;
            }
        }

        private static bool SetDebugger(string subKey, string value)
        {
            if (string.Equals(GetDebugger(subKey), value, StringComparison.OrdinalIgnoreCase)) return false;

            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(subKey))
            {
                key.SetValue("Debugger", value, RegistryValueKind.String);
            }
            return true;
        }

        private static bool ClearDebugger(string subKey)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey, true))
            {
                if (key == null || key.GetValue("Debugger") == null) return false;
                key.DeleteValue("Debugger", false);
                return true;
            }
        }
    }
}

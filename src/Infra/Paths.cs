using System;
using System.IO;

namespace Toolkit
{
    /// <summary>
    /// Два разных места:
    ///  - ExeDir: где лежат Toolkit.exe и tkguard.exe (при запуске через StartHidden.vbs это %TEMP%);
    ///  - BaseDir: «домашняя» папка Toolkit с config.json, profiles, reg, data, tools, logs
    ///    (обычно общая папка на сервере или игровом диске).
    /// Если домашняя папка не задана, это папка с exe, как раньше.
    /// </summary>
    internal static class Paths
    {
        public static string ExeDir;
        public static string BaseDir;

        /// <summary>Приоритет: ключ --home, переменная окружения TOOLKIT_HOME, папка с exe.</summary>
        public static void Init(string homeOption)
        {
            ExeDir = TrimSlash(AppDomain.CurrentDomain.BaseDirectory);

            string home = homeOption;
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("TOOLKIT_HOME");

            BaseDir = string.IsNullOrWhiteSpace(home)
                ? ExeDir
                : TrimSlash(Path.GetFullPath(Environment.ExpandEnvironmentVariables(home.Trim().Trim('"'))));
        }

        public static string ProfilesDir
        {
            get { return Path.Combine(BaseDir, "profiles"); }
        }

        public static string TaskbarProfile
        {
            get { return Path.Combine(ProfilesDir, "taskbar.json"); }
        }

        public static string ShortcutProfilesDir
        {
            get { return Path.Combine(ProfilesDir, "shortcuts"); }
        }

        public static string RegFilesDir
        {
            get { return Path.Combine(BaseDir, "reg"); }
        }

        public static string DefaultConfig
        {
            get { return Path.Combine(BaseDir, "config.json"); }
        }

        /// <summary>Папка логов: общая для всех ПК, файл в ней свой у каждого (см. Log).</summary>
        public static string LogsDir
        {
            get { return Path.Combine(BaseDir, "logs"); }
        }

        /// <summary>Разворачивает %VAR% и делает относительные пути относительными к Toolkit (BaseDir).</summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (Path.IsPathRooted(expanded)) return expanded;

            return Path.GetFullPath(Path.Combine(BaseDir, expanded));
        }

        /// <summary>Убирает хвостовой слэш, кроме корня диска: «D:» без слэша означает текущую папку диска.</summary>
        private static string TrimSlash(string path)
        {
            string root = Path.GetPathRoot(path);
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return path;
            return path.TrimEnd('\\', '/');
        }
    }
}

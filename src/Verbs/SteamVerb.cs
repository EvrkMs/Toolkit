using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace Toolkit
{
    /// <summary>
    /// Подменяет папки Steam джанкшенами на общий диск игр (аналог steam_links.bat):
    /// appcache, depotcache и steam\games (иконки .url-ярлыков). Иконка ярлыка — это
    /// steam\games\&lt;хэш&gt;.ico, на который ссылается IconFile= в .url, поэтому путь
    /// должен быть доступен сразу после загрузки.
    /// </summary>
    internal static class SteamVerb
    {
        private const string C = "STEAM";

        public static int Run(ToolkitConfig cfg)
        {
            SteamConfig sc = cfg.Steam;
            if (!sc.Enabled)
            {
                Log.Write(C, "SKIP disabled");
                return 0;
            }

            string steamDir = ResolveSteamDir(sc);
            if (steamDir == null)
            {
                Log.Write(C, "SKIP Steam не найден (installPath=" + sc.InstallPath + ")");
                return 0;
            }

            string source = Paths.Resolve(sc.Source);
            if (!WaitForSource(source, sc.WaitSeconds))
            {
                Log.Write(C, "FAIL источник недоступен за " + sc.WaitSeconds + " c: " + source);
                return ExitCode.Failed;
            }

            Log.Write(C, "steam=" + steamDir + " source=" + source);

            int failed = 0;
            bool steamClosed = false;

            foreach (SteamLink link in sc.Links)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Path) || string.IsNullOrWhiteSpace(link.Source)) continue;

                string linkPath = Path.Combine(steamDir, link.Path);
                string target = Path.Combine(source, link.Source);

                try
                {
                    if (!Directory.Exists(target))
                    {
                        Log.Write(C, "SKIP " + link.Path + ": нет " + target);
                        continue;
                    }

                    if (SymlinkVerb.IsLinkTo(linkPath, target))
                    {
                        Log.Write(C, "OK " + link.Path + " already linked");
                        continue;
                    }

                    // Открытые Steam'ом файлы не дадут снести папку.
                    if (!steamClosed)
                    {
                        CloseSteam();
                        steamClosed = true;
                    }

                    if (!Relink(linkPath, target)) failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Write(C, "ERROR " + link.Path + " :: " + ex.Message);
                }
            }

            Log.Write(C, "DONE failed=" + failed);
            return failed == 0 ? 0 : ExitCode.Failed;
        }

        private static bool Relink(string linkPath, string target)
        {
            bool isReparse = false;
            if (Directory.Exists(linkPath))
            {
                isReparse = (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
            }

            if (Directory.Exists(linkPath) && !isReparse)
            {
                // Реальная папка: недостающее переносим на общий диск, ничего не затирая,
                // чтобы уже скачанные иконки не пропали.
                int merged = MergeMissing(linkPath, target);
                Directory.Delete(linkPath, true);
                Log.Write(C, "removed real folder " + linkPath + " merged=" + merged);
            }
            else if (isReparse)
            {
                // Rmdir на ссылке удаляет только её саму, не цель.
                Directory.Delete(linkPath, false);
                Log.Write(C, "removed old link " + linkPath);
            }
            else if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            string parent = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            string output;
            int code = ProcessRunner.Run("cmd.exe",
                "/c mklink /J " + ProcessRunner.Quote(linkPath) + " " + ProcessRunner.Quote(target), 20, out output);

            if (code != 0 || !SymlinkVerb.IsLinkTo(linkPath, target))
            {
                Log.Write(C, "FAIL link " + linkPath + " -> " + target + " code=" + code + " " + output.Trim());
                return false;
            }

            Log.Write(C, "link: " + linkPath + " -> " + target);
            return true;
        }

        private static int MergeMissing(string from, string to)
        {
            int copied = 0;
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(from.Length).TrimStart('\\');
                string dest = Path.Combine(to, rel);
                if (File.Exists(dest)) continue;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(file, dest);
                    copied++;
                }
                catch (Exception ex)
                {
                    Log.Write(C, "WARN merge " + rel + " :: " + ex.Message);
                }
            }
            return copied;
        }

        private static void CloseSteam()
        {
            foreach (Process p in Process.GetProcessesByName("steam"))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                    Log.Write(C, "steam.exe closed");
                }
                catch
                {
                }
            }
        }

        /// <summary>Игровой диск на PXE-загрузке появляется не сразу.</summary>
        private static bool WaitForSource(string source, int seconds)
        {
            for (int waited = 0; waited <= seconds; waited++)
            {
                if (Directory.Exists(source)) return true;
                Thread.Sleep(1000);
            }
            return false;
        }

        /// <summary>steam icons: докачать .ico для всех установленных игр в steam\games.</summary>
        public static int DownloadIcons(ToolkitConfig cfg)
        {
            string steamDir = ResolveSteamDir(cfg.Steam);
            if (steamDir == null)
            {
                Log.Write(C, "FAIL Steam не найден");
                return ExitCode.Failed;
            }

            int downloaded = SteamIcons.DownloadInstalled(steamDir);
            Log.Write(C, "icons downloaded=" + downloaded);
            return 0;
        }

        private static string ResolveSteamDir(SteamConfig sc)
        {
            if (!string.IsNullOrWhiteSpace(sc.InstallPath))
            {
                string explicitPath = Environment.ExpandEnvironmentVariables(sc.InstallPath);
                return Directory.Exists(explicitPath) ? explicitPath : null;
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    string path = key == null ? null : key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
                }
            }
            catch
            {
            }

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(fallback) ? fallback : null;
        }
    }
}

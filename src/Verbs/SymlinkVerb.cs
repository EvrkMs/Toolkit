using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Toolkit
{
    internal static class SymlinkVerb
    {
        private const int SYMBOLIC_LINK_FLAG_DIRECTORY = 0x1;
        private const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;

        private sealed class Options
        {
            public string Root;
            public string Source;
            public string Dest;
            public bool Recreate = true;
            public bool DryRun;
            public bool IncludeProgramData = true;
            public bool IncludeProgramFiles;
        }

        private sealed class Mapping
        {
            public string Name;
            public string SourceRoot;
            public string DestinationRoot;
        }

        private sealed class Counters
        {
            public int Created;
            public int Removed;
            public int Skipped;
            public int Failed;
            public int AlreadyOk;
        }

        /// <summary>Путь уже является ссылкой (или иным способом ведёт) на нужную цель.</summary>
        internal static bool IsLinkTo(string path, string expectedTarget)
        {
            if (!Directory.Exists(path)) return false;

            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
            }
            catch
            {
                return false;
            }

            string actual = NativeMethods.GetFinalPath(path);
            string expected = NativeMethods.GetFinalPath(expectedTarget);
            if (actual == null || expected == null) return false;

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        public static int Run(ToolkitConfig cfg, Cli cli)
        {
            if (!cfg.Symlink.Enabled && cli == null)
            {
                Log.Write("SYMLINK", "SKIP disabled");
                return 0;
            }

            Options opt = new Options();
            opt.Root = cfg.Symlink.Root;
            opt.Recreate = cfg.Symlink.Recreate.HasValue ? cfg.Symlink.Recreate.Value : true;
            opt.IncludeProgramData = cfg.Symlink.IncludeProgramData.HasValue ? cfg.Symlink.IncludeProgramData.Value : true;
            opt.IncludeProgramFiles = cfg.Symlink.IncludeProgramFiles.HasValue ? cfg.Symlink.IncludeProgramFiles.Value : false;

            if (cli != null)
            {
                opt.Root = cli.Option("--root", opt.Root);
                opt.Source = cli.Option("--source", null);
                opt.Dest = cli.Option("--dest", null);
                opt.Recreate = cli.Bool("--recreate", opt.Recreate);
                opt.DryRun = cli.Flag("--dry-run");
                opt.IncludeProgramData = cli.Bool("--include-programdata", opt.IncludeProgramData);
                opt.IncludeProgramFiles = cli.Bool("--include-program-files", opt.IncludeProgramFiles);
            }

            bool singleMode = !string.IsNullOrWhiteSpace(opt.Source) && !string.IsNullOrWhiteSpace(opt.Dest);
            List<Mapping> mappings = singleMode ? BuildSingleMapping(opt) : BuildAutoMappings(opt);

            if (mappings.Count == 0)
            {
                Log.Write("SYMLINK", "no valid mappings (root=" + opt.Root + ")");
                return 10;
            }

            Counters total = new Counters();
            foreach (Mapping m in mappings)
            {
                ProcessMapping(m, opt, total);
            }

            Log.Write("SYMLINK", "DONE created=" + total.Created +
                                 " alreadyOk=" + total.AlreadyOk +
                                 " removed=" + total.Removed +
                                 " skipped=" + total.Skipped +
                                 " failed=" + total.Failed +
                                 (opt.DryRun ? " (dry-run)" : ""));

            return total.Failed > 0 ? 10 : 0;
        }

        private static List<Mapping> BuildSingleMapping(Options opt)
        {
            List<Mapping> list = new List<Mapping>();
            string src = Paths.Resolve(opt.Source);
            string dst = Environment.ExpandEnvironmentVariables(opt.Dest);

            if (!string.IsNullOrWhiteSpace(src) && !string.IsNullOrWhiteSpace(dst))
            {
                Mapping m = new Mapping();
                m.Name = "single";
                m.SourceRoot = src;
                m.DestinationRoot = dst;
                list.Add(m);
            }

            return list;
        }

        private static List<Mapping> BuildAutoMappings(Options opt)
        {
            List<Mapping> list = new List<Mapping>();
            // Относительный путь считается от папки Toolkit (например data\GameFix).
            string root = Paths.Resolve(opt.Root ?? "");

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return list;
            }

            AddMappingIfExists(list, @"AppData\Local", Path.Combine(root, "AppData", "Local"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AddMappingIfExists(list, @"AppData\LocalLow", Path.Combine(root, "AppData", "LocalLow"),
                GetLocalLowPath());
            AddMappingIfExists(list, @"AppData\Roaming", Path.Combine(root, "AppData", "Roaming"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

            if (opt.IncludeProgramData)
            {
                AddMappingIfExists(list, "ProgramData", Path.Combine(root, "ProgramData"),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            }

            if (opt.IncludeProgramFiles)
            {
                AddMappingIfExists(list, "Program Files", Path.Combine(root, "Program Files"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                AddMappingIfExists(list, "Program Files (x86)", Path.Combine(root, "Program Files (x86)"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            }

            return list;
        }

        private static void AddMappingIfExists(List<Mapping> list, string name, string src, string dst)
        {
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst)) return;
            if (!Directory.Exists(src)) return;

            Mapping m = new Mapping();
            m.Name = name;
            m.SourceRoot = src;
            m.DestinationRoot = dst;
            list.Add(m);
        }

        private static void ProcessMapping(Mapping map, Options opt, Counters total)
        {
            string srcRoot = Environment.ExpandEnvironmentVariables(map.SourceRoot);
            string dstRoot = Environment.ExpandEnvironmentVariables(map.DestinationRoot);

            if (!Directory.Exists(srcRoot))
            {
                Log.Write("SYMLINK", "SKIP source missing: " + srcRoot);
                return;
            }

            if (string.IsNullOrWhiteSpace(dstRoot))
            {
                Log.Write("SYMLINK", "SKIP destination empty: " + map.Name);
                return;
            }

            if (!opt.DryRun)
            {
                Directory.CreateDirectory(dstRoot);
            }

            Log.Write("SYMLINK", "mapping " + map.Name + ": " + srcRoot + " => " + dstRoot);

            foreach (string srcDir in Directory.GetDirectories(srcRoot))
            {
                string name = Path.GetFileName(srcDir);
                string dstDir = Path.Combine(dstRoot, name);

                try
                {
                    // Ссылка уже ведёт куда надо - не трогаем. Снос и пересоздание
                    // рабочей ссылки бессмыслен и ловит гонку: если папку в этот
                    // момент держит открытый хендл, удаление откладывается, и
                    // CreateSymbolicLink падает с ERROR_ALREADY_EXISTS (183).
                    if (IsLinkTo(dstDir, srcDir))
                    {
                        total.AlreadyOk++;
                        continue;
                    }

                    if (Directory.Exists(dstDir))
                    {
                        if (!opt.Recreate)
                        {
                            total.Skipped++;
                            continue;
                        }

                        if (!opt.DryRun) Directory.Delete(dstDir, true);
                        total.Removed++;
                    }
                    else if (File.Exists(dstDir))
                    {
                        if (!opt.Recreate)
                        {
                            total.Skipped++;
                            continue;
                        }

                        if (!opt.DryRun) File.Delete(dstDir);
                        total.Removed++;
                    }

                    if (opt.DryRun)
                    {
                        total.Created++;
                        Log.Write("SYMLINK", "would link: " + dstDir + " -> " + srcDir);
                        continue;
                    }

                    bool ok = NativeMethods.CreateSymbolicLink(dstDir, srcDir,
                        SYMBOLIC_LINK_FLAG_DIRECTORY | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE);

                    if (!ok)
                    {
                        ok = NativeMethods.CreateSymbolicLink(dstDir, srcDir, SYMBOLIC_LINK_FLAG_DIRECTORY);
                    }

                    if (ok)
                    {
                        total.Created++;
                        Log.Write("SYMLINK", "link: " + dstDir + " -> " + srcDir);
                    }
                    else
                    {
                        total.Failed++;
                        Log.Write("SYMLINK", "FAIL link: " + dstDir + " (win32=" + Marshal.GetLastWin32Error() + ")");
                    }
                }
                catch (Exception ex)
                {
                    total.Failed++;
                    Log.Write("SYMLINK", "ERROR " + dstDir + " :: " + ex.Message);
                }
            }
        }

        private static string GetLocalLowPath()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile)) return null;
            return Path.Combine(profile, "AppData", "LocalLow");
        }
    }
}

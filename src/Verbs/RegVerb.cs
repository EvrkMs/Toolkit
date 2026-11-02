using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toolkit
{
    internal static class RegVerb
    {
        public static int Run(ToolkitConfig cfg)
        {
            if (!cfg.Reg.Enabled)
            {
                Log.Write("REG", "SKIP disabled");
                return 0;
            }

            List<string> files = Collect(cfg.Reg);
            if (files.Count == 0)
            {
                Log.Write("REG", "no .reg files (expected in " + Paths.RegFilesDir + ")");
                return 0;
            }

            Log.Write("REG", "files discovered: " + files.Count);

            string regExe = Path.Combine(Environment.SystemDirectory, "reg.exe");
            int failed = 0;

            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    failed++;
                    Log.Write("REG", "missing: " + file);
                    continue;
                }

                int code = ProcessRunner.Run(regExe, "import " + ProcessRunner.Quote(file), 30);
                if (code == 0)
                {
                    Log.Write("REG", "OK: " + file);
                }
                else
                {
                    failed++;
                    Log.Write("REG", "FAIL(" + code + "): " + file);
                }
            }

            Log.Write("REG", "DONE failed=" + failed);
            return failed == 0 ? 0 : 10;
        }

        private static List<string> Collect(RegConfig cfg)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(Paths.RegFilesDir))
            {
                string[] found = Directory.GetFiles(Paths.RegFilesDir, "*.reg", SearchOption.TopDirectoryOnly);
                foreach (string file in found.OrderBy(Path.GetFileName))
                {
                    string full = Path.GetFullPath(file);
                    if (seen.Add(full)) result.Add(full);
                }
            }

            if (cfg.Files != null)
            {
                foreach (string file in cfg.Files)
                {
                    string resolved = Paths.Resolve(file);
                    if (string.IsNullOrWhiteSpace(resolved)) continue;
                    if (seen.Add(resolved)) result.Add(resolved);
                }
            }

            return result;
        }
    }
}

using System;
using System.IO;

namespace Toolkit
{
    internal static class NvidiaVerb
    {
        public static int Run(ToolkitConfig cfg)
        {
            if (!cfg.Nvidia.Enabled)
            {
                Log.Write("NVIDIA", "SKIP disabled");
                return 0;
            }

            string exePath = Paths.Resolve(cfg.Nvidia.ExePath);
            string profilePath = Paths.Resolve(cfg.Nvidia.ProfilePath);

            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                Log.Write("NVIDIA", "exe missing: " + exePath);
                return ExitCode.Failed;
            }

            if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
            {
                Log.Write("NVIDIA", "profile missing: " + profilePath);
                return ExitCode.Failed;
            }

            // Ключ импорта задаётся конфигом: набор поддерживаемых аргументов
            // отличается между сборками nvidiaProfileInspector.
            string template = string.IsNullOrWhiteSpace(cfg.Nvidia.Arguments) ? "{profile}" : cfg.Nvidia.Arguments;
            string arguments = template.Replace("{profile}", ProcessRunner.Quote(profilePath));

            int code = ProcessRunner.RunAndSuppressWindows(exePath, arguments, cfg.Nvidia.TimeoutSeconds);

            if (code == 0)
            {
                Log.Write("NVIDIA", "OK args=" + arguments);
                return 0;
            }

            if (code == ProcessRunner.ExitTimeout)
            {
                Log.Write("NVIDIA", "TIMEOUT after " + cfg.Nvidia.TimeoutSeconds + "s args=" + arguments +
                                    " (профиль, скорее всего, НЕ применён: проверь поддерживаемый ключ импорта)");
            }
            else
            {
                Log.Write("NVIDIA", "FAIL(" + code + ") args=" + arguments);
            }

            return ExitCode.Failed;
        }
    }
}

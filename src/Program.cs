using System;
using System.IO;

namespace Toolkit
{
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitFatal = 1;
        private const int ExitUsage = 2;

        private static int Main(string[] rawArgs)
        {
            ConsoleBridge.AttachToParent();
            Paths.BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            Cli cli = Cli.Parse(rawArgs);
            bool verbose = cli.Flag("--verbose") || cli.Flag("-v");

            Log.Init(Paths.BaseDir, verbose);
            NativeMethods.EnableDpiAwareness();

            if (string.IsNullOrWhiteSpace(cli.Verb) || cli.Flag("--help") || cli.Flag("-h") ||
                string.Equals(cli.Verb, "help", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                return string.IsNullOrWhiteSpace(cli.Verb) ? ExitUsage : ExitOk;
            }

            ToolkitConfig cfg;
            try
            {
                cfg = ConfigLoader.Load(cli.Option("--config", Paths.DefaultConfig));
            }
            catch (Exception ex)
            {
                Log.Write("MAIN", "FATAL config: " + ex.Message);
                return ExitFatal;
            }

            ExplorerRestart.Policy = cli.Option("--explorer-restart", cfg.Explorer.RestartPolicy);

            Log.Write("MAIN", "START verb=" + cli.Verb +
                              (string.IsNullOrWhiteSpace(cli.SubVerb) ? "" : " " + cli.SubVerb) +
                              " explorerRestart=" + ExplorerRestart.Policy);

            int code;
            try
            {
                code = Dispatch(cli, cfg);
            }
            catch (Exception ex)
            {
                Log.Write("MAIN", "FATAL " + ex);
                return ExitFatal;
            }

            // Любой шаг, попросивший перезапуск и не обслуженный внутри verb'а,
            // обслуживается здесь — ровно один раз за прогон.
            ExplorerRestart.Commit();

            Log.Write("MAIN", "DONE code=" + code);
            return code;
        }

        private static int Dispatch(Cli cli, ToolkitConfig cfg)
        {
            switch (cli.Verb.ToLowerInvariant())
            {
                case "run":
                    return RunVerb.Run(cfg);

                case "reg":
                    return RegVerb.Run(cfg);

                case "guard":
                    return GuardVerb.Run(cfg, cli);

                case "nvidia":
                    return NvidiaVerb.Run(cfg);

                case "resolutions":
                    return Math.Max(ResolutionsVerb.Run(cfg), ResolutionsVerb.EnsureMaxRefresh(cfg));

                case "scaling":
                    return ScalingVerb.Run(cfg);

                case "audio":
                    return AudioVerb.Run(cfg);

                case "symlink":
                    return SymlinkVerb.Run(cfg, cli);

                case "steam":
                    if (string.Equals(cli.SubVerb, "icons", StringComparison.OrdinalIgnoreCase))
                    {
                        return SteamVerb.DownloadIcons(cfg);
                    }
                    return SteamVerb.Run(cfg);

                case "taskbar":
                    return DispatchSaveLoad(cli, TaskbarVerb.Save, TaskbarVerb.Load, "taskbar");

                case "shortcuts":
                    return DispatchSaveLoad(cli,
                        delegate { return ShortcutsVerb.Save(cli); },
                        delegate { return ShortcutsVerb.Load(cli); },
                        "shortcuts");

                case "explorer":
                    if (!string.Equals(cli.SubVerb, "restart", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Write("MAIN", "usage: Toolkit.exe explorer restart");
                        return ExitUsage;
                    }
                    ExplorerRestart.Request("manual request");
                    return ExitOk;

                default:
                    Log.Write("MAIN", "unknown verb: " + cli.Verb);
                    PrintHelp();
                    return ExitUsage;
            }
        }

        private static int DispatchSaveLoad(Cli cli, Func<int> save, Func<int> load, string verb)
        {
            if (string.Equals(cli.SubVerb, "save", StringComparison.OrdinalIgnoreCase)) return save();
            if (string.Equals(cli.SubVerb, "load", StringComparison.OrdinalIgnoreCase)) return load();

            // save перезаписывает эталонный профиль, поэтому режим всегда явный.
            Log.Write("MAIN", "usage: Toolkit.exe " + verb + " save|load");
            return ExitUsage;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Toolkit - утилиты эталонного образа ПК-клуба (.NET Framework 4.8, x64)");
            Console.WriteLine();
            Console.WriteLine("  Toolkit.exe run                     весь профиль загрузки");
            Console.WriteLine("  Toolkit.exe reg                     импорт .reg из Toolkit\\reg");
            Console.WriteLine("  Toolkit.exe nvidia                  применить профиль NVIDIA");
            Console.WriteLine("  Toolkit.exe resolutions             добавить разрешения NVIDIA из конфига");
            Console.WriteLine("  Toolkit.exe scaling                 масштабирование NVIDIA на ГП");
            Console.WriteLine("  Toolkit.exe audio                   громкость, микрофон, AGC, boost");
            Console.WriteLine("  Toolkit.exe symlink [--dry-run]     симлинки GameFix");
            Console.WriteLine("  Toolkit.exe steam                   джанкшены Steam: appcache, depotcache, steam\\games");
            Console.WriteLine("  Toolkit.exe steam icons             докачать .ico установленных игр в steam\\games");
            Console.WriteLine("  Toolkit.exe taskbar save|load      панель задач");
            Console.WriteLine("  Toolkit.exe shortcuts save|load     ярлыки и раскладка иконок");
            Console.WriteLine("  Toolkit.exe explorer restart        перезапустить explorer");
            Console.WriteLine();
            Console.WriteLine("Общие ключи:");
            Console.WriteLine("  --config <path>                     по умолчанию Toolkit\\config.json");
            Console.WriteLine("  --explorer-restart auto|never|force перекрывает политику из конфига");
            Console.WriteLine("  --verbose, -v                       дублировать лог в консоль");
            Console.WriteLine();
            Console.WriteLine("shortcuts:");
            Console.WriteLine("  разрешение определяется автоматически с текущего экрана;");
            Console.WriteLine("  --resolution <WxH> только переопределяет имя профиля.");
            Console.WriteLine();
            Console.WriteLine("symlink: --root --source --dest --recreate <bool>");
            Console.WriteLine("         --include-programdata <bool> --include-program-files <bool>");
        }
    }
}

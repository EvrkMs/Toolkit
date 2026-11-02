using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Toolkit
{
    internal static class RunVerb
    {
        public static int Run(ToolkitConfig cfg)
        {
            int worst = 0;

            // Первым и синхронно: GameFix.bat убивает explorer уже через ~10 с после входа.
            worst = Math.Max(worst, Safe("GUARD", delegate { return GuardVerb.Ensure(cfg); }));

            // NVIDIA ждём отдельно, в самом конце: она единственная долгая и при этом
            // ни с чем не связана. Раньше панель и ярлыки стояли в очереди за ней,
            // и неудачный вызов драйвера задерживал всю видимую часть загрузки.
            Task<int> nvidiaJob = null;
            if (cfg.Nvidia.Enabled)
            {
                nvidiaJob = Task.Run(delegate { return Safe("NVIDIA", delegate { return NvidiaVerb.Run(cfg); }); });
            }

            // Steam: ссылки, потом докачка иконок по сети. Сеть может тянуться долго,
            // а оболочке эти иконки не нужны, поэтому ждём в самом конце, как NVIDIA.
            Task<int> steamJob = null;
            if (cfg.Steam.Enabled)
            {
                steamJob = Task.Run(delegate
                {
                    int links = Safe("STEAM", delegate { return SteamVerb.Run(cfg); });
                    int icons = cfg.Steam.DownloadIcons
                        ? Safe("STEAMICO", delegate { return SteamVerb.DownloadIcons(cfg); })
                        : 0;
                    return Math.Max(links, icons);
                });
            }

            // Эти трогают файлы и реестр, поэтому завершаем их до перезапуска explorer.
            List<Task<int>> jobs = new List<Task<int>>();

            if (cfg.Reg.Enabled) jobs.Add(Task.Run(delegate { return Safe("REG", delegate { return RegVerb.Run(cfg); }); }));
            if (cfg.Audio.Enabled) jobs.Add(Task.Run(delegate { return Safe("AUDIO", delegate { return AudioVerb.Run(cfg); }); }));
            if (cfg.Symlink.Enabled) jobs.Add(Task.Run(delegate { return Safe("SYMLINK", delegate { return SymlinkVerb.Run(cfg, null); }); }));

            // Оба шага могут на миг переключить экран, поэтому идут друг за другом
            // и заканчиваются до раскладки иконок.
            if (cfg.Resolutions.Enabled || cfg.Scaling.Enabled)
            {
                jobs.Add(Task.Run(delegate
                {
                    int res = cfg.Resolutions.Enabled ? Safe("RES", delegate { return ResolutionsVerb.Run(cfg); }) : 0;
                    int scale = cfg.Scaling.Enabled ? Safe("SCALE", delegate { return ScalingVerb.Run(cfg); }) : 0;
                    return Math.Max(res, scale);
                }));
            }

            Task.WaitAll(jobs.ToArray());
            foreach (Task<int> job in jobs)
            {
                worst = Math.Max(worst, job.Result);
            }

            // Панель задач идёт до перезапуска explorer: её состояние живёт
            // в реестре и подхватывается только новым процессом explorer.
            if (cfg.Taskbar.Enabled)
            {
                worst = Math.Max(worst, Safe("TASKBAR", delegate { return TaskbarVerb.Load(); }));
            }

            // Перезапуск здесь, а не в самом конце: раскладка иконок применяется
            // сообщениями живому SysListView32, и убийство explorer после неё
            // откатило бы позиции к сохранённым в реестре.
            bool restarted = ExplorerRestart.Commit();
            if (restarted)
            {
                ExplorerRestart.WaitForShell(15000);
            }

            if (cfg.Shortcuts.Enabled)
            {
                worst = Math.Max(worst, Safe("SHORTCUT", delegate { return ShortcutsVerb.Load(null); }));
            }

            if (steamJob != null)
            {
                steamJob.Wait();
                worst = Math.Max(worst, steamJob.Result);
            }

            if (nvidiaJob != null)
            {
                nvidiaJob.Wait();
                worst = Math.Max(worst, nvidiaJob.Result);
            }

            // Самым последним: к этому моменту explorer, драйвер и остальные шаги отработали.
            worst = Math.Max(worst, Safe("RES", delegate { return ResolutionsVerb.EnsureMaxRefresh(cfg); }));

            return worst;
        }

        private static int Safe(string component, Func<int> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Log.Write(component, "ERROR " + ex.Message);
                return 10;
            }
        }
    }
}

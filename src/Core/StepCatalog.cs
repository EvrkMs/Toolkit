using System;
using System.Collections.Generic;

namespace Toolkit
{
    /// <summary>
    /// Состав шагов команды run и их зависимости. Порядок записи значения не имеет:
    /// последовательность определяют только Requires (после каких шагов) и WaitFor (чего ждать).
    ///
    /// Правило: шаг ждёт только то, что ему действительно нужно. Поэтому перезапуск explorer
    /// не зависит от драйвера NVIDIA и звука, а шаги, которым нужен драйвер, помечены WaitFor(GpuDriver)
    /// и не мешают остальным.
    /// </summary>
    internal static class StepCatalog
    {
        public static List<Step> Build(ToolkitConfig cfg)
        {
            List<Step> steps = new List<Step>();

            // Без зависимостей: стартуют сразу и идут параллельно.
            if (cfg.Reg.Enabled)
            {
                Add(steps, "reg", delegate { return RegVerb.Run(cfg); }, null, null);
            }

            if (cfg.Symlink.Enabled)
            {
                Add(steps, "symlink", delegate { return SymlinkVerb.Run(cfg, null); }, null, null);
            }

            // Steam: ссылки, потом докачка иконок по сети. Оболочке иконки не нужны, на остальное не влияют.
            if (cfg.Steam.Enabled)
            {
                Add(steps, "steam", delegate { return SteamVerb.Run(cfg); }, null, null);

                if (cfg.Steam.DownloadIcons)
                {
                    Add(steps, "steam.icons", delegate { return SteamVerb.DownloadIcons(cfg); },
                        After("steam"), null);
                }
            }

            // Профиль NVIDIA: нужен загруженный драйвер.
            if (cfg.Nvidia.Enabled)
            {
                Add(steps, "nvidia", delegate { return NvidiaVerb.Run(cfg); },
                    null, Gated(Readiness.GpuDriver));
            }

            // Звук. Отключение лишних устройств вывода ждёт драйвер и появление NVIDIA-аудио, иначе
            // его просто не успеют отключить. Уровни идут после: дефолтным к этому моменту уже стал USB.
            bool usbOnly = cfg.Audio.UsbOnly.HasValue && cfg.Audio.UsbOnly.Value;
            if (cfg.Audio.Enabled)
            {
                if (usbOnly)
                {
                    Add(steps, "audio.devices", delegate { return AudioVerb.RunDevices(cfg); },
                        null, Gated(Readiness.NvidiaAudio));
                }

                Add(steps, "audio.levels", delegate { return AudioVerb.RunLevels(cfg); },
                    After("audio.devices"), null);
            }

            // Оба шага могут на миг переключить экран, поэтому идут друг за другом. Нужен драйвер.
            if (cfg.Resolutions.Enabled)
            {
                Add(steps, "resolutions", delegate { return ResolutionsVerb.Run(cfg); },
                    null, Gated(Readiness.GpuDriver));
            }

            if (cfg.Scaling.Enabled)
            {
                Add(steps, "scaling", delegate { return ScalingVerb.Run(cfg); },
                    After("resolutions"), Gated(Readiness.GpuDriver));
            }

            // Панель задач: её состояние живёт в реестре и подхватывается только новым процессом explorer,
            // поэтому идёт после .reg и до перезапуска.
            if (cfg.Taskbar.Enabled)
            {
                Add(steps, "taskbar", delegate { return TaskbarVerb.Load(); }, After("reg"), null);
            }

            // Перезапуск explorer один раз за прогон, и только после того, как .reg и панель задач
            // сказали своё слово. От драйвера, звука и разрешения он не зависит.
            // (Commit возвращает управление, когда оболочка уже поднялась.)
            Add(steps, "explorer-restart", delegate
            {
                ExplorerRestart.Commit();
                return 0;
            }, After("reg", "taskbar"), null);

            // Раскладка иконок применяется сообщениями живому SysListView32: нужен новый explorer,
            // готовая оболочка и окончательное разрешение. Иначе позиции откатятся или собьются.
            if (cfg.Shortcuts.Enabled)
            {
                Add(steps, "shortcuts", delegate { return ShortcutsVerb.Load(null); },
                    After("explorer-restart", "resolutions", "scaling"), Gated(Readiness.Shell));
            }

            // Самым последним: к этому моменту explorer, драйвер и остальные шаги отработали.
            if (cfg.Resolutions.Enabled)
            {
                Add(steps, "max-refresh", delegate { return ResolutionsVerb.EnsureMaxRefresh(cfg); },
                    After("shortcuts", "nvidia", "resolutions", "scaling"), Gated(Readiness.GpuDriver));
            }

            return steps;
        }

        private static void Add(List<Step> steps, string name, Func<int> action, string[] requires, Gate[] waitFor)
        {
            Step step = new Step();
            step.Name = name;
            step.Action = action;
            if (requires != null) step.Requires = requires;
            if (waitFor != null) step.WaitFor = waitFor;
            steps.Add(step);
        }

        private static string[] After(params string[] names)
        {
            return names;
        }

        private static Gate[] Gated(params Gate[] gates)
        {
            return gates;
        }
    }
}

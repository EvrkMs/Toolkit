using System;
using System.IO;

namespace Toolkit
{
    /// <summary>Общие gate'ы прогона. Создаются один раз после загрузки конфига (Program).</summary>
    internal static class Readiness
    {
        public static GpuDriverGate GpuDriver;
        public static ShellGate Shell;

        public static void Init(ToolkitConfig cfg)
        {
            GatesConfig g = cfg.Gates;

            GpuDriver = new GpuDriverGate(cfg.Resolutions.Display, g.GpuDriverSeconds.Value * 1000);
            Shell = new ShellGate(g.ShellSeconds.Value * 1000);
        }
    }

    /// <summary>
    /// Драйвер NVIDIA загружен: дисплей перешёл с базового адаптера Windows на адаптер NVIDIA.
    /// Если драйвер вообще не установлен (нет nvapi64.dll), ждать нечего и gate сразу возвращает false.
    /// </summary>
    internal sealed class GpuDriverGate : Gate
    {
        private readonly string _display;
        private readonly int _timeoutMs;

        public GpuDriverGate(string display, int timeoutMs)
        {
            _display = display;
            _timeoutMs = timeoutMs;
        }

        public override string Name
        {
            get { return "GpuDriver"; }
        }

        protected override bool Poll()
        {
            if (!File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll")))
            {
                Log.Write("GATE", "GpuDriver: nvapi64.dll нет, драйвер NVIDIA не установлен, не ждём");
                return false;
            }

            return PollUntil(delegate
            {
                string display = string.IsNullOrWhiteSpace(_display) ? DisplayInterop.PrimaryDisplayName() : _display;
                if (string.IsNullOrWhiteSpace(display)) return false;

                string adapter = DisplayInterop.AdapterDescription(display);
                return adapter != null && adapter.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
            }, _timeoutMs, 500);
        }
    }

    /// <summary>Оболочка Windows запущена: есть панель задач и рабочий стол (нужно раскладке иконок).</summary>
    internal sealed class ShellGate : Gate
    {
        private readonly int _timeoutMs;

        public ShellGate(int timeoutMs)
        {
            _timeoutMs = timeoutMs;
        }

        public override string Name
        {
            get { return "Shell"; }
        }

        protected override bool Poll()
        {
            return ExplorerRestart.WaitForShell(_timeoutMs);
        }
    }
}

using System;
using System.Runtime.Serialization;

namespace Toolkit
{
    /// <summary>
    /// Таймауты ожидания готовности системы. Gate только задерживает шаг: по таймауту шаг всё равно запускается.
    /// </summary>
    [DataContract]
    internal sealed class GatesConfig
    {
        /// <summary>Сколько секунд ждать, пока дисплей перейдёт на драйвер NVIDIA. Не задано — 60.</summary>
        [DataMember(Name = "gpuDriverSeconds")]
        public int? GpuDriverSeconds { get; set; }

        /// <summary>Сколько секунд ждать оболочку Windows перед раскладкой иконок. Не задано — 15.</summary>
        [DataMember(Name = "shellSeconds")]
        public int? ShellSeconds { get; set; }

        public void ApplyDefaults()
        {
            GpuDriverSeconds = Clamp(GpuDriverSeconds, 60);
            ShellSeconds = Clamp(ShellSeconds, 15);
        }

        private static int Clamp(int? value, int fallback)
        {
            if (!value.HasValue) return fallback;
            return value.Value < 0 ? 0 : value.Value;
        }
    }
}

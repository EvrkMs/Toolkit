using System;
using System.Collections.Generic;

namespace Toolkit
{
    /// <summary>
    /// Добавляет пользовательские разрешения NVIDIA в список доступных (как
    /// «Создать пользовательское разрешение» в панели), но не переключает на них.
    /// Уже существующие режимы пропускаются, поэтому при каждой загрузке экран
    /// мигает только если чего-то не хватает.
    /// </summary>
    internal static class ResolutionsVerb
    {
        private const string C = "RES";

        public static int Run(ToolkitConfig cfg)
        {
            ResolutionsConfig rc = cfg.Resolutions;
            if (!rc.Enabled)
            {
                Log.Write(C, "SKIP disabled");
                return 0;
            }

            if (rc.Modes == null || rc.Modes.Length == 0)
            {
                Log.Write(C, "SKIP no modes in config");
                return 0;
            }

            string display = string.IsNullOrWhiteSpace(rc.Display) ? DisplayInterop.PrimaryDisplayName() : rc.Display;
            if (string.IsNullOrWhiteSpace(display))
            {
                Log.Write(C, "FAIL primary display not found");
                return 10;
            }

            WaitForGpuDriver(display);

            uint displayId;
            try
            {
                NvApi.Init();
                displayId = NvApi.DisplayIdByName(display);
            }
            catch (DllNotFoundException)
            {
                Log.Write(C, "SKIP nvapi64.dll not found (no NVIDIA driver)");
                return 0;
            }
            catch (NvApiException ex)
            {
                Log.Write(C, "FAIL " + ex.Message);
                return 10;
            }

            bool native = !string.Equals(rc.Timing, "real", StringComparison.OrdinalIgnoreCase);
            int timingType = ParseTimingStandard(rc.TimingStandard);

            DEVMODE current = DisplayInterop.CurrentMode(display);
            int nativeWidth, nativeHeight;
            if (!DisplayInterop.TryGetNativeSize(display, out nativeWidth, out nativeHeight))
            {
                nativeWidth = current.dmPelsWidth;
                nativeHeight = current.dmPelsHeight;
                Log.Write(C, "native size unknown, using current " + nativeWidth + "x" + nativeHeight);
            }

            // Текущая частота на холодной загрузке может быть 60 Гц от базового драйвера,
            // поэтому по умолчанию берём максимум, который монитор отдаёт на родном разрешении.
            int maxRefresh = DisplayInterop.MaxRefresh(display, nativeWidth, nativeHeight);
            float defaultRefresh = Math.Max(maxRefresh, current.dmDisplayFrequency);
            Log.Write(C, "default refresh=" + defaultRefresh + " (max@native=" + maxRefresh + ", current=" + current.dmDisplayFrequency + ")");

            Log.Write(C, display + " id=0x" + displayId.ToString("X8") + " native=" + nativeWidth + "x" + nativeHeight +
                         " current=" + current.dmPelsWidth + "x" + current.dmPelsHeight + "@" + current.dmDisplayFrequency +
                         " timing=" + (native ? "native" : "real"));

            List<NV_CUSTOM_DISPLAY> existing = NvApi.ListCustom(displayId);
            int failed = 0;
            bool trialStarted = false;

            try
            {
                foreach (ResolutionMode mode in rc.Modes)
                {
                    if (mode == null || mode.Width <= 0 || mode.Height <= 0) continue;

                    string name = mode.Width + "x" + mode.Height;
                    float refresh = mode.Refresh > 0 ? mode.Refresh : defaultRefresh;

                    if (IsCustom(existing, mode.Width, mode.Height))
                    {
                        Log.Write(C, "SKIP " + name + " already added");
                        continue;
                    }

                    if (DisplayInterop.HasMode(display, mode.Width, mode.Height))
                    {
                        Log.Write(C, "SKIP " + name + " already in mode list");
                        continue;
                    }

                    // Native: рабочий стол WxH, а на монитор идёт его родной сигнал — масштабирует
                    // видеокарта. Больше родного так не сделать, для этого нужен timing=real.
                    if (native && (mode.Width > nativeWidth || mode.Height > nativeHeight))
                    {
                        Log.Write(C, "SKIP " + name + " exceeds native " + nativeWidth + "x" + nativeHeight);
                        continue;
                    }

                    try
                    {
                        NV_TIMING timing = native
                            ? NvApi.GetTiming(displayId, nativeWidth, nativeHeight, refresh, timingType)
                            : NvApi.GetTiming(displayId, mode.Width, mode.Height, refresh, timingType);

                        trialStarted = true;
                        NvApi.AddCustom(displayId, mode.Width, mode.Height, timing);

                        Log.Write(C, "OK " + name + " signal=" + timing.HVisible + "x" + timing.VVisible +
                                     "@" + (timing.etc.rrx1k / 1000.0).ToString("0.###") +
                                     " pclk=" + (timing.pclk / 100.0).ToString("0.##") + "MHz");
                    }
                    catch (NvApiException ex)
                    {
                        failed++;
                        Log.Write(C, "FAIL " + name + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                // Пробный режим NVAPI висит до смены режима: возвращаем тот, что в реестре.
                if (trialStarted)
                {
                    int reset = DisplayInterop.ResetMode(display);
                    if (reset != 0) Log.Write(C, "WARN reset mode returned " + reset);
                }
            }

            Log.Write(C, "DONE failed=" + failed);
            return failed == 0 ? 0 : 10;
        }

        /// <summary>
        /// Ждёт, пока дисплей перейдёт на драйвер NVIDIA и список частот перестанет меняться.
        /// Раньше шаг стартовал сразу и мог поймать базовый адаптер с 60 Гц.
        /// </summary>
        private static void WaitForGpuDriver(string display)
        {
            const int driverTimeoutMs = 60000;
            const int stableReadsNeeded = 3;

            int waited = 0;
            bool nvidia = false;
            while (waited < driverTimeoutMs)
            {
                string adapter = DisplayInterop.AdapterDescription(display);
                if (adapter != null && adapter.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    nvidia = true;
                    break;
                }

                System.Threading.Thread.Sleep(500);
                waited += 500;
            }

            if (!nvidia)
            {
                Log.Write(C, "WARN driver NVIDIA не появился за " + (waited / 1000) + " c, adapter=" + DisplayInterop.AdapterDescription(display));
                return;
            }

            // Драйвер загружен, но EDID и список режимов могут дочитываться: ждём стабильного максимума.
            int nativeW, nativeH;
            if (!DisplayInterop.TryGetNativeSize(display, out nativeW, out nativeH))
            {
                nativeW = DisplayInterop.CurrentMode(display).dmPelsWidth;
                nativeH = DisplayInterop.CurrentMode(display).dmPelsHeight;
            }

            int last = -1;
            int stable = 0;
            while (waited < driverTimeoutMs && stable < stableReadsNeeded)
            {
                int now = DisplayInterop.MaxRefresh(display, nativeW, nativeH);
                stable = (now == last && now > 0) ? stable + 1 : 0;
                last = now;
                if (stable >= stableReadsNeeded) break;

                System.Threading.Thread.Sleep(500);
                waited += 500;
            }

            Log.Write(C, "driver ready after " + (waited / 1000.0).ToString("0.#") + " c, max@" + nativeW + "x" + nativeH + "=" + last + "Hz");
        }

        /// <summary>
        /// Вызывается последним шагом прогона. После загрузки Windows часто остаётся на 60 Гц,
        /// хотя монитор отдаёт больше: если максимальная частота не выставлена, поднимаем
        /// текущий режим до неё при том же разрешении. Иначе ничего не трогаем.
        /// </summary>
        public static int EnsureMaxRefresh(ToolkitConfig cfg)
        {
            if (!cfg.Resolutions.Enabled) return 0;

            string display = string.IsNullOrWhiteSpace(cfg.Resolutions.Display)
                ? DisplayInterop.PrimaryDisplayName()
                : cfg.Resolutions.Display;
            if (string.IsNullOrWhiteSpace(display))
            {
                Log.Write(C, "WARN refresh: primary display not found");
                return 10;
            }

            ApplyMaxRefresh(display);
            return 0;
        }

        private static void ApplyMaxRefresh(string display)
        {
            DEVMODE cur = DisplayInterop.CurrentMode(display);
            int max = DisplayInterop.MaxRefresh(display, cur.dmPelsWidth, cur.dmPelsHeight);

            if (max <= cur.dmDisplayFrequency)
            {
                Log.Write(C, "refresh OK " + cur.dmDisplayFrequency + "Hz (max=" + max + ")");
                return;
            }

            int rc = DisplayInterop.SetRefresh(display, max);
            Log.Write(C, rc == 0
                ? "refresh " + cur.dmDisplayFrequency + " -> " + max + "Hz"
                : "WARN refresh set to " + max + "Hz failed code=" + rc);
        }

        private static bool IsCustom(List<NV_CUSTOM_DISPLAY> existing, int width, int height)
        {
            foreach (NV_CUSTOM_DISPLAY cd in existing)
            {
                if (cd.width == width && cd.height == height) return true;
            }
            return false;
        }

        private static int ParseTimingStandard(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "edid": return NvApi.TimingEdid;
                case "dmt": return NvApi.TimingDmt;
                case "dmt-rb": return NvApi.TimingDmtRb;
                case "cvt": return NvApi.TimingCvt;
                case "cvt-rb": return NvApi.TimingCvtRb;
                case "gtf": return NvApi.TimingGtf;
                default: return NvApi.TimingAuto;
            }
        }
    }
}

using System;

namespace Toolkit
{
    /// <summary>
    /// Режим масштабирования NVIDIA на ГП («Регулировка размера и положения рабочего стола»).
    /// Если режим уже нужный, ничего не делает — экран не мигает.
    /// </summary>
    internal static class ScalingVerb
    {
        private const string C = "SCALE";

        public static int Run(ToolkitConfig cfg)
        {
            ScalingConfig sc = cfg.Scaling;
            if (!sc.Enabled)
            {
                Log.Write(C, "SKIP disabled");
                return 0;
            }

            int scaling;
            if (!TryParseMode(sc.Mode, out scaling))
            {
                Log.Write(C, "FAIL unknown mode: " + sc.Mode + " (fullscreen|aspect|none|integer)");
                return ExitCode.Failed;
            }

            string display = string.IsNullOrWhiteSpace(sc.Display) ? DisplayInterop.PrimaryDisplayName() : sc.Display;
            if (string.IsNullOrWhiteSpace(display))
            {
                Log.Write(C, "FAIL primary display not found");
                return ExitCode.Failed;
            }

            try
            {
                NvApi.Init();
                uint displayId = NvApi.DisplayIdByName(display);

                int previous;
                if (NvApi.SetScaling(displayId, scaling, out previous))
                {
                    Log.Write(C, "OK " + display + " scaling " + previous + " -> " + scaling + " (" + sc.Mode + ")");
                }
                else
                {
                    Log.Write(C, "SKIP " + display + " already " + sc.Mode);
                }
                return 0;
            }
            catch (DllNotFoundException)
            {
                Log.Write(C, "SKIP nvapi64.dll not found (no NVIDIA driver)");
                return 0;
            }
            catch (NvApiException ex)
            {
                Log.Write(C, "FAIL " + ex.Message);
                return ExitCode.Failed;
            }
        }

        private static bool TryParseMode(string value, out int scaling)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "fullscreen": scaling = NvApi.ScalingFullScreen; return true;
                case "aspect": scaling = NvApi.ScalingAspectRatio; return true;
                case "none": scaling = NvApi.ScalingNoScaling; return true;
                case "integer": scaling = NvApi.ScalingInteger; return true;
                default: scaling = 0; return false;
            }
        }
    }
}

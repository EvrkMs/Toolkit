using System;
using System.Runtime.InteropServices;

namespace Toolkit
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    /// <summary>GDI- и CCD-вызовы для режимов экрана (без драйверной специфики).</summary>
    internal static class DisplayInterop
    {
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
        private const uint QDC_ONLY_ACTIVE_PATHS = 2;
        private const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
        private const uint DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint Low;
            public int High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public LUID sourceAdapterId;
            public uint sourceId, sourceModeInfoIdx, sourceStatusFlags;
            public LUID targetAdapterId;
            public uint targetId, targetModeInfoIdx, outputTechnology, rotation, scaling;
            public uint refreshNum, refreshDen;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint targetStatusFlags;
            public uint flags;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public uint type, size;
            public LUID adapterId;
            public uint id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        // Заголовок (20 байт) + width/height + DISPLAYCONFIG_TARGET_MODE (48 байт, выравнивание 8).
        [StructLayout(LayoutKind.Explicit, Size = 80)]
        private struct DISPLAYCONFIG_TARGET_PREFERRED_MODE
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(4)] public uint size;
            [FieldOffset(8)] public LUID adapterId;
            [FieldOffset(16)] public uint id;
            [FieldOffset(20)] public uint width;
            [FieldOffset(24)] public uint height;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE dm);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string deviceName, IntPtr dm, IntPtr hwnd, uint flags, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
            ref uint numModes, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE request);

        /// <summary>\\.\DISPLAYn основного монитора или null.</summary>
        public static string PrimaryDisplayName()
        {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0 &&
                    (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0)
                {
                    return dd.DeviceName;
                }
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }

            return null;
        }

        /// <summary>Описание видеоадаптера, к которому привязан \\.\DISPLAYn (до драйвера — «Microsoft Basic Display Adapter»).</summary>
        public static string AdapterDescription(string gdiName)
        {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if (string.Equals(dd.DeviceName, gdiName, StringComparison.OrdinalIgnoreCase)) return dd.DeviceString;
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }

            return null;
        }

        /// <summary>Максимальная частота среди режимов WxH, которые Windows сейчас предлагает; 0 — таких режимов нет.</summary>
        public static int MaxRefresh(string deviceName, int width, int height)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int max = 0;

            for (int i = 0; EnumDisplaySettings(deviceName, i, ref dm); i++)
            {
                if (dm.dmPelsWidth == width && dm.dmPelsHeight == height && dm.dmDisplayFrequency > max)
                {
                    max = dm.dmDisplayFrequency;
                }
            }

            return max;
        }

        public static DEVMODE CurrentMode(string deviceName)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
            {
                throw new InvalidOperationException("EnumDisplaySettings failed for " + deviceName);
            }
            return dm;
        }

        /// <summary>Есть ли WxH среди режимов, которые Windows уже предлагает.</summary>
        public static bool HasMode(string deviceName, int width, int height)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            for (int i = 0; EnumDisplaySettings(deviceName, i, ref dm); i++)
            {
                if (dm.dmPelsWidth == width && dm.dmPelsHeight == height) return true;
            }

            return false;
        }

        /// <summary>Ставит частоту (Гц) при текущем разрешении и сохраняет её в реестр; 0 — успех.</summary>
        public static int SetRefresh(string deviceName, int hz)
        {
            const int DM_DISPLAYFREQUENCY = 0x400000;
            const uint CDS_UPDATEREGISTRY = 0x1;

            DEVMODE dm = CurrentMode(deviceName);
            dm.dmDisplayFrequency = hz;
            dm.dmFields = DM_DISPLAYFREQUENCY;

            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DEVMODE)));
            try
            {
                Marshal.StructureToPtr(dm, ptr, false);
                return ChangeDisplaySettingsEx(deviceName, ptr, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>Возвращает режим из реестра — сбрасывает пробный режим NVAPI.</summary>
        public static int ResetMode(string deviceName)
        {
            return ChangeDisplaySettingsEx(deviceName, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }

        /// <summary>
        /// Родное (preferred по EDID) разрешение монитора. Текущий режим для этого не годится:
        /// ПК может уже стоять в 1280x960.
        /// </summary>
        public static bool TryGetNativeSize(string gdiName, out int width, out int height)
        {
            width = 0;
            height = 0;

            uint numPaths, numModes;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes) != 0) return false;

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[numModes];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0) return false;

            for (int i = 0; i < numPaths; i++)
            {
                DISPLAYCONFIG_SOURCE_DEVICE_NAME source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.type = DEVICE_INFO_GET_SOURCE_NAME;
                source.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                source.adapterId = paths[i].sourceAdapterId;
                source.id = paths[i].sourceId;

                if (DisplayConfigGetDeviceInfo(ref source) != 0 ||
                    !string.Equals(source.viewGdiDeviceName, gdiName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DISPLAYCONFIG_TARGET_PREFERRED_MODE preferred = new DISPLAYCONFIG_TARGET_PREFERRED_MODE();
                preferred.type = DEVICE_INFO_GET_TARGET_PREFERRED_MODE;
                preferred.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_PREFERRED_MODE));
                preferred.adapterId = paths[i].targetAdapterId;
                preferred.id = paths[i].targetId;

                if (DisplayConfigGetDeviceInfo(ref preferred) != 0 || preferred.width == 0 || preferred.height == 0) return false;

                width = (int)preferred.width;
                height = (int)preferred.height;
                return true;
            }

            return false;
        }
    }
}

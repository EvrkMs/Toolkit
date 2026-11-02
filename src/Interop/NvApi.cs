using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Toolkit
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct NV_TIMINGEXT
    {
        public uint flag;
        public ushort rr;
        public uint rrx1k;
        public uint aspect;
        public ushort rep;
        public uint status;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)] public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NV_TIMING
    {
        public ushort HVisible, HBorder, HFrontPorch, HSyncWidth, HTotal;
        public byte HSyncPol;
        public ushort VVisible, VBorder, VFrontPorch, VSyncWidth, VTotal;
        public byte VSyncPol;
        public ushort interlaced;
        public uint pclk; // единицы по 10 кГц
        public NV_TIMINGEXT etc;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NV_VIEWPORTF
    {
        public float x, y, w, h;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NV_CUSTOM_DISPLAY
    {
        public uint version;
        public uint width, height, depth;
        public uint colorFormat;
        public NV_VIEWPORTF srcPartition;
        public float xRatio, yRatio;
        public NV_TIMING timing;
        public uint hwModeSetOnly;
    }

    internal sealed class NvApiException : Exception
    {
        public readonly int Status;

        public NvApiException(string what, int status, string text)
            : base(what + " failed: " + status + " (" + text + ")")
        {
            Status = status;
        }
    }

    /// <summary>
    /// NVAPI без SDK: функции достаются из nvapi64.dll драйвера через nvapi_QueryInterface
    /// по их ID (так же делает NvAPIWrapper). Сборка x64, поэтому только nvapi64.dll.
    /// </summary>
    internal static class NvApi
    {
        public const int TimingAuto = 1;
        public const int TimingEdid = 2;
        public const int TimingDmt = 3;
        public const int TimingDmtRb = 4;
        public const int TimingCvt = 5;
        public const int TimingCvtRb = 6;
        public const int TimingGtf = 7;

        private const int NVAPI_END_ENUMERATION = -7;
        private const int NVAPI_INCOMPATIBLE_STRUCT_VERSION = -9;

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_Initialize();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_GetErrorMessage(int status, StringBuilder msg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_GetDisplayIdByDisplayName([MarshalAs(UnmanagedType.LPStr)] string name, out uint displayId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_GetTiming(uint displayId, IntPtr timingInput, ref NV_TIMING timing);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_TryCustomDisplay(ref uint displayIds, uint count, ref NV_CUSTOM_DISPLAY cd);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_RevertCustomDisplayTrial(ref uint displayIds, uint count);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_SaveCustomDisplay(ref uint displayIds, uint count, uint isThisOutputIdOnly, uint isThisMonitorIdOnly);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_EnumCustomDisplay(uint displayId, uint index, ref NV_CUSTOM_DISPLAY cd);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_GetDisplayConfig(ref uint pathInfoCount, IntPtr pathInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NvAPI_DISP_SetDisplayConfig(uint pathInfoCount, IntPtr pathInfo, uint flags);

        // NV_SCALING
        public const int ScalingFullScreen = 2;     // GPU_SCALING_TO_NATIVE — «Во весь экран»
        public const int ScalingNoScaling = 3;      // GPU_SCANOUT_TO_NATIVE — «Без масштабирования»
        public const int ScalingAspectRatio = 5;    // GPU_SCALING_TO_ASPECT_SCANOUT_TO_NATIVE — «Формат изображения»
        public const int ScalingInteger = 8;        // GPU_INTEGER_ASPECT_SCALING — «Целочисленное»

        // Раскладки x64 из nvapi.h.
        private const int PathInfoSize = 48;        // NV_DISPLAYCONFIG_PATH_INFO_V2
        private const int PathTargetCountOffset = 8;
        private const int PathTargetInfoOffset = 16;
        private const int PathSourceModeOffset = 24;
        private const int TargetInfoSize = 24;      // NV_DISPLAYCONFIG_PATH_TARGET_INFO_V2
        private const int TargetDetailsOffset = 8;
        private const int SourceModeSize = 32;      // NV_DISPLAYCONFIG_SOURCE_MODE_INFO_V1
        private const int AdvancedInfoSize = 128;   // NV_DISPLAYCONFIG_PATH_ADVANCED_TARGET_INFO
        private const int AdvancedScalingOffset = 8;
        private const uint NV_DISPLAYCONFIG_SAVE_TO_PERSISTENCE = 2;

        private static readonly object _lock = new object();
        private static bool _initialized;

        private static T Fn<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface(id);
            if (p == IntPtr.Zero)
            {
                throw new NotSupportedException(string.Format("NVAPI {0} (0x{1:X8}) not available in this driver", typeof(T).Name, id));
            }
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        /// <summary>Бросает DllNotFoundException, если драйвера NVIDIA нет.</summary>
        public static void Init()
        {
            lock (_lock)
            {
                if (_initialized) return;

                // Раскладка должна совпадать с nvapi.h, иначе драйвер отвергнет версию структуры.
                if (Marshal.SizeOf(typeof(NV_TIMING)) != 96 || Marshal.SizeOf(typeof(NV_CUSTOM_DISPLAY)) != 144)
                {
                    throw new InvalidOperationException("unexpected NVAPI struct size");
                }

                Check(Fn<NvAPI_Initialize>(0x0150E828)(), "NvAPI_Initialize");
                _initialized = true;
            }
        }

        public static string ErrorText(int status)
        {
            StringBuilder sb = new StringBuilder(64);
            try
            {
                Fn<NvAPI_GetErrorMessage>(0x6C2D048C)(status, sb);
            }
            catch
            {
            }
            return sb.ToString();
        }

        private static void Check(int status, string what)
        {
            if (status != 0) throw new NvApiException(what, status, ErrorText(status));
        }

        public static uint DisplayIdByName(string gdiName)
        {
            uint id;
            Check(Fn<NvAPI_DISP_GetDisplayIdByDisplayName>(0xAE457190)(gdiName, out id), "GetDisplayIdByDisplayName(" + gdiName + ")");
            return id;
        }

        public static NV_TIMING GetTiming(uint displayId, int width, int height, float refresh, int type)
        {
            NvAPI_DISP_GetTiming fn = Fn<NvAPI_DISP_GetTiming>(0x175167E9);

            // Размер NV_TIMING_INPUT зависит от того, как компилятор упаковал битовые поля
            // NV_TIMING_FLAG, поэтому пробуем известные варианты: драйвер отвечает
            // INCOMPATIBLE_STRUCT_VERSION на неверный.
            int[] sizes = { 32, 28, 24 };
            int status = 0;

            foreach (int size in sizes)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                    Marshal.WriteInt32(buf, 0, size | (1 << 16));
                    Marshal.WriteInt32(buf, 4, width);
                    Marshal.WriteInt32(buf, 8, height);
                    Marshal.WriteInt32(buf, 12, BitConverter.ToInt32(BitConverter.GetBytes(refresh), 0));
                    Marshal.WriteInt32(buf, size - 4, type);

                    NV_TIMING timing = new NV_TIMING();
                    status = fn(displayId, buf, ref timing);
                    if (status == 0) return timing;
                    if (status != NVAPI_INCOMPATIBLE_STRUCT_VERSION) break;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }

            Check(status, "GetTiming");
            throw new InvalidOperationException();
        }

        private static NV_CUSTOM_DISPLAY NewCustomDisplay()
        {
            NV_CUSTOM_DISPLAY cd = new NV_CUSTOM_DISPLAY();
            cd.version = (uint)Marshal.SizeOf(typeof(NV_CUSTOM_DISPLAY)) | (1u << 16);
            return cd;
        }

        public static List<NV_CUSTOM_DISPLAY> ListCustom(uint displayId)
        {
            NvAPI_DISP_EnumCustomDisplay fn = Fn<NvAPI_DISP_EnumCustomDisplay>(0xA2072D59);
            List<NV_CUSTOM_DISPLAY> result = new List<NV_CUSTOM_DISPLAY>();

            for (uint i = 0; ; i++)
            {
                NV_CUSTOM_DISPLAY cd = NewCustomDisplay();
                int status = fn(displayId, i, ref cd);
                if (status == NVAPI_END_ENUMERATION) break;
                Check(status, "EnumCustomDisplay");
                result.Add(cd);
            }

            return result;
        }

        /// <summary>
        /// Добавляет режим в список доступных. NVAPI умеет сохранить только режим,
        /// который сейчас пробуется, поэтому экран на мгновение переключается;
        /// вернуть исходный режим должен вызывающий (DisplayInterop.ResetMode).
        /// </summary>
        public static void AddCustom(uint displayId, int width, int height, NV_TIMING timing)
        {
            NV_CUSTOM_DISPLAY cd = NewCustomDisplay();
            cd.width = (uint)width;
            cd.height = (uint)height;
            cd.depth = 32;
            cd.srcPartition.w = 1;
            cd.srcPartition.h = 1;
            cd.xRatio = 1;
            cd.yRatio = 1;
            cd.timing = timing;

            uint id = displayId;
            Check(Fn<NvAPI_DISP_TryCustomDisplay>(0x1F7DB630)(ref id, 1, ref cd), "TryCustomDisplay");

            // isThisOutputIdOnly = 0: режим следует за монитором на любой порт;
            // isThisMonitorIdOnly = 1: только для этой модели монитора.
            int status = Fn<NvAPI_DISP_SaveCustomDisplay>(0x49882876)(ref id, 1, 0, 1);
            if (status != 0)
            {
                Fn<NvAPI_DISP_RevertCustomDisplayTrial>(0xCBBD40F0)(ref id, 1);
                Check(status, "SaveCustomDisplay");
            }
        }

        /// <summary>
        /// Режим масштабирования на ГП для монитора («Регулировка размера и положения рабочего
        /// стола»). Возвращает false, если он уже такой — тогда смены режима (и мигания) нет.
        /// </summary>
        public static bool SetScaling(uint displayId, int scaling, out int previous)
        {
            NvAPI_DISP_GetDisplayConfig get = Fn<NvAPI_DISP_GetDisplayConfig>(0x11ABCCF8);
            NvAPI_DISP_SetDisplayConfig set = Fn<NvAPI_DISP_SetDisplayConfig>(0x5D8CF8DE);
            List<IntPtr> allocations = new List<IntPtr>();

            try
            {
                // Трёхпроходный протокол из nvapi.h: число путей -> число целей -> данные.
                uint count = 0;
                Check(get(ref count, IntPtr.Zero), "GetDisplayConfig(count)");

                IntPtr paths = Alloc(allocations, (int)count * PathInfoSize);
                for (int i = 0; i < count; i++)
                {
                    Marshal.WriteInt32(paths, i * PathInfoSize, PathInfoSize | (2 << 16));
                }
                Check(get(ref count, paths), "GetDisplayConfig(paths)");

                for (int i = 0; i < count; i++)
                {
                    int path = i * PathInfoSize;
                    int targetCount = Marshal.ReadInt32(paths, path + PathTargetCountOffset);

                    IntPtr targets = Alloc(allocations, targetCount * TargetInfoSize);
                    for (int j = 0; j < targetCount; j++)
                    {
                        IntPtr details = Alloc(allocations, AdvancedInfoSize);
                        Marshal.WriteInt32(details, 0, AdvancedInfoSize | (1 << 16));
                        Marshal.WriteIntPtr(targets, j * TargetInfoSize + TargetDetailsOffset, details);
                    }

                    Marshal.WriteIntPtr(paths, path + PathTargetInfoOffset, targets);
                    Marshal.WriteIntPtr(paths, path + PathSourceModeOffset, Alloc(allocations, SourceModeSize));
                }
                Check(get(ref count, paths), "GetDisplayConfig(details)");

                IntPtr found = IntPtr.Zero;
                for (int i = 0; i < count && found == IntPtr.Zero; i++)
                {
                    int path = i * PathInfoSize;
                    int targetCount = Marshal.ReadInt32(paths, path + PathTargetCountOffset);
                    IntPtr targets = Marshal.ReadIntPtr(paths, path + PathTargetInfoOffset);

                    for (int j = 0; j < targetCount; j++)
                    {
                        if ((uint)Marshal.ReadInt32(targets, j * TargetInfoSize) == displayId)
                        {
                            found = Marshal.ReadIntPtr(targets, j * TargetInfoSize + TargetDetailsOffset);
                            break;
                        }
                    }
                }

                if (found == IntPtr.Zero)
                {
                    throw new NvApiException("GetDisplayConfig", 0, "displayId 0x" + displayId.ToString("X8") + " not in active paths");
                }

                previous = Marshal.ReadInt32(found, AdvancedScalingOffset);
                if (previous == scaling) return false;

                Marshal.WriteInt32(found, AdvancedScalingOffset, scaling);
                Check(set(count, paths, NV_DISPLAYCONFIG_SAVE_TO_PERSISTENCE), "SetDisplayConfig");
                return true;
            }
            finally
            {
                foreach (IntPtr p in allocations) Marshal.FreeHGlobal(p);
            }
        }

        private static IntPtr Alloc(List<IntPtr> allocations, int size)
        {
            IntPtr p = Marshal.AllocHGlobal(Math.Max(size, 1));
            allocations.Add(p);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            return p;
        }
    }
}

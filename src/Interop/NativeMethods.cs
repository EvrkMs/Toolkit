using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Toolkit
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    internal static class NativeMethods
    {
        public const int SW_HIDE = 0;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        public static void HideProcessWindows(int pid)
        {
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint wndPid;
                GetWindowThreadProcessId(hWnd, out wndPid);
                if (wndPid == pid && IsWindowVisible(hWnd))
                {
                    ShowWindowAsync(hWnd, SW_HIDE);
                }
                return true;
            }, IntPtr.Zero);
        }

        /// <summary>
        /// Разрешение в физических пикселях. Без DPI-awareness (см. Program.Main)
        /// GetSystemMetrics вернул бы виртуализированный размер при масштабе != 100%,
        /// и профиль ярлыков уехал бы и по имени, и по координатам.
        /// </summary>
        public static string GetScreenResolution()
        {
            return GetSystemMetrics(SM_CXSCREEN) + "x" + GetSystemMetrics(SM_CYSCREEN);
        }

        public static void EnableDpiAwareness()
        {
            try
            {
                // Windows 10 1703+: per-monitor v2.
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch { }

            try
            {
                SetProcessDPIAware();
            }
            catch { }
        }

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

        private const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        /// <summary>
        /// Разрешённый (без символьных ссылок) путь. В .NET Framework 4.8 нет
        /// Directory.ResolveLinkTarget, поэтому спрашиваем саму систему.
        /// Возвращает null, если путь недоступен.
        /// </summary>
        public static string GetFinalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            IntPtr handle = CreateFile(path, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE) return null;

            try
            {
                StringBuilder buffer = new StringBuilder(1024);
                int length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                if (length == 0) return null;

                string result = buffer.ToString();
                if (result.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    result = result.Substring(4);
                }

                return result.TrimEnd('\\');
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandle(IntPtr hFile, StringBuilder lpszFilePath, int cchFilePath, int dwFlags);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}

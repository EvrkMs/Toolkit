using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Toolkit
{
    internal sealed class DesktopItem
    {
        public int Index;
        public string Name;
        public int X;
        public int Y;
    }

    /// <summary>
    /// Чтение и раскладка иконок рабочего стола. SysListView32 живёт в explorer.exe,
    /// поэтому структуры LVITEM аллоцируются в его адресном пространстве —
    /// разрядность Toolkit обязана совпадать с разрядностью explorer (см. /platform:x64).
    /// </summary>
    internal static class DesktopListView
    {
        private const uint LVM_FIRST = 0x1000;
        private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;
        private const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;
        private const int LVIF_TEXT = 0x0001;

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const int GWL_STYLE = -16;
        private const int LVS_AUTOARRANGE = 0x0100;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public static IntPtr Find()
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", "Program Manager");
            IntPtr shellView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shellView == IntPtr.Zero)
            {
                // При включённых обоях-слайдшоу рабочий стол переезжает
                // в отдельное окно WorkerW.
                IntPtr found = IntPtr.Zero;
                NativeMethods.EnumWindows(delegate(IntPtr top, IntPtr lParam)
                {
                    IntPtr def = NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (def != IntPtr.Zero)
                    {
                        found = def;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                shellView = found;
            }

            if (shellView == IntPtr.Zero) return IntPtr.Zero;
            return NativeMethods.FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
        }

        public static bool TryGetItems(int timeoutMs, out IntPtr listView, out List<DesktopItem> items)
        {
            listView = IntPtr.Zero;
            items = new List<DesktopItem>();

            int waited = 0;
            while (waited < timeoutMs)
            {
                listView = Find();
                if (listView != IntPtr.Zero)
                {
                    items = ReadItems(listView);
                    if (items.Count > 0) return true;
                }

                Thread.Sleep(200);
                waited += 200;
            }

            return false;
        }

        private const int StablePollMs = 250;
        private const int StablePollsRequired = 3;

        /// <summary>
        /// Ждёт, пока рабочий стол не только появится, но и достроится: сразу после
        /// перезапуска explorer список наполняется постепенно, и раскладка,
        /// применённая к половине иконок, промахивается по остальным.
        /// Признак готовности — счётчик элементов не менялся StablePollsRequired опросов подряд.
        /// </summary>
        public static bool TryGetStableItems(int timeoutMs, out IntPtr listView, out List<DesktopItem> items, out bool stabilized)
        {
            listView = IntPtr.Zero;
            items = new List<DesktopItem>();
            stabilized = false;

            int waited = 0;
            int lastCount = -1;
            int stableStreak = 0;

            while (waited < timeoutMs)
            {
                IntPtr view = Find();
                int count = 0;

                if (view != IntPtr.Zero)
                {
                    count = (int)NativeMethods.SendMessage(view, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                }

                if (count > 0 && count == lastCount)
                {
                    stableStreak++;

                    if (stableStreak >= StablePollsRequired)
                    {
                        List<DesktopItem> read = ReadItems(view);
                        if (read.Count > 0)
                        {
                            listView = view;
                            items = read;
                            stabilized = true;
                            Log.Write("SHORTCUT", "desktop ready: items=" + read.Count + " after " + waited + "ms");
                            return true;
                        }

                        // Список есть, а прочитать не вышло — пробуем дальше.
                        stableStreak = 0;
                    }
                }
                else
                {
                    stableStreak = 0;
                }

                lastCount = count;
                Thread.Sleep(StablePollMs);
                waited += StablePollMs;
            }

            // Таймаут: работаем с тем, что успело появиться, но честно помечаем это.
            IntPtr lastView = Find();
            if (lastView != IntPtr.Zero)
            {
                List<DesktopItem> read = ReadItems(lastView);
                if (read.Count > 0)
                {
                    listView = lastView;
                    items = read;
                    Log.Write("SHORTCUT", "WARN desktop did not settle in " + timeoutMs + "ms, proceeding with items=" + read.Count);
                    return true;
                }
            }

            return false;
        }

        public static bool SetPosition(IntPtr listView, int index, int x, int y)
        {
            int packed = ((y & 0xFFFF) << 16) | (x & 0xFFFF);
            IntPtr ok = NativeMethods.SendMessage(listView, LVM_SETITEMPOSITION, (IntPtr)index, (IntPtr)packed);
            return ok != IntPtr.Zero;
        }

        public static bool DisableAutoArrange(IntPtr listView)
        {
            int style = NativeMethods.GetWindowLong(listView, GWL_STYLE);
            bool wasOn = (style & LVS_AUTOARRANGE) != 0;
            if (!wasOn) return false;

            NativeMethods.SetWindowLong(listView, GWL_STYLE, style & ~LVS_AUTOARRANGE);
            NativeMethods.SetWindowPos(listView, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            Log.Write("SHORTCUT", "auto-arrange disabled for desktop list view");
            return true;
        }

        public static List<DesktopItem> ReadItems(IntPtr listView)
        {
            List<DesktopItem> items = new List<DesktopItem>();

            int count = (int)NativeMethods.SendMessage(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0) return items;

            uint pid;
            NativeMethods.GetWindowThreadProcessId(listView, out pid);

            IntPtr hProcess = NativeMethods.OpenProcess(
                PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE,
                false, pid);
            if (hProcess == IntPtr.Zero) return items;

            try
            {
                int textChars = 520;
                int textBytes = textChars * 2;
                int lvItemSize = Marshal.SizeOf(typeof(LVITEM));
                int ptSize = Marshal.SizeOf(typeof(POINT));
                int total = lvItemSize + textBytes + ptSize;

                IntPtr remote = NativeMethods.VirtualAllocEx(hProcess, IntPtr.Zero, (UIntPtr)total,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remote == IntPtr.Zero) return items;

                try
                {
                    IntPtr remoteText = IntPtr.Add(remote, lvItemSize);
                    IntPtr remotePoint = IntPtr.Add(remote, lvItemSize + textBytes);

                    for (int i = 0; i < count; i++)
                    {
                        LVITEM lvi = new LVITEM();
                        lvi.mask = LVIF_TEXT;
                        lvi.iItem = i;
                        lvi.iSubItem = 0;
                        lvi.cchTextMax = textChars;
                        lvi.pszText = remoteText;

                        byte[] lviBytes = StructureToBytes(lvi);
                        IntPtr written;
                        NativeMethods.WriteProcessMemory(hProcess, remote, lviBytes, lviBytes.Length, out written);

                        NativeMethods.SendMessage(listView, LVM_GETITEMTEXTW, (IntPtr)i, remote);

                        byte[] textBuffer = new byte[textBytes];
                        IntPtr read;
                        NativeMethods.ReadProcessMemory(hProcess, remoteText, textBuffer, textBuffer.Length, out read);

                        string name = Encoding.Unicode.GetString(textBuffer);
                        int z = name.IndexOf('\0');
                        if (z >= 0) name = name.Substring(0, z);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        NativeMethods.SendMessage(listView, LVM_GETITEMPOSITION, (IntPtr)i, remotePoint);
                        byte[] ptBuffer = new byte[ptSize];
                        NativeMethods.ReadProcessMemory(hProcess, remotePoint, ptBuffer, ptBuffer.Length, out read);
                        POINT pt = BytesToStructure<POINT>(ptBuffer);

                        DesktopItem item = new DesktopItem();
                        item.Index = i;
                        item.Name = name;
                        item.X = pt.X;
                        item.Y = pt.Y;
                        items.Add(item);
                    }
                }
                finally
                {
                    NativeMethods.VirtualFreeEx(hProcess, remote, UIntPtr.Zero, MEM_RELEASE);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }

            return items;
        }

        public static void Refresh()
        {
            NativeMethods.SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.SHChangeNotify(0x00002000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }

        private static byte[] StructureToBytes<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
                return arr;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private static T BytesToStructure<T>(byte[] arr) where T : struct
        {
            IntPtr ptr = Marshal.AllocHGlobal(arr.Length);
            try
            {
                Marshal.Copy(arr, 0, ptr, arr.Length);
                return (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM
        {
            public int mask;
            public int iItem;
            public int iSubItem;
            public int state;
            public int stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public int cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}

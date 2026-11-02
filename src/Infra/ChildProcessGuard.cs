using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Toolkit
{
    /// <summary>
    /// Привязывает запускаемые процессы к job-объекту с KILL_ON_JOB_CLOSE, чтобы
    /// они умирали вместе с Toolkit. Без этого прерванный прогон (Ctrl+C, выключение
    /// на PXE-загрузке) оставлял в системе живой nvidiaProfileInspector.
    /// </summary>
    internal static class ChildProcessGuard
    {
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        private const int JobObjectExtendedLimitInformation = 9;

        private static readonly object _lock = new object();
        private static IntPtr _job = IntPtr.Zero;
        private static bool _initialized;

        public static void Attach(Process process)
        {
            if (process == null) return;

            try
            {
                IntPtr job = EnsureJob();
                if (job == IntPtr.Zero) return;

                if (!AssignProcessToJobObject(job, process.Handle))
                {
                    Log.Write("MAIN", "WARN cannot attach child to job: win32=" + Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                // Не критично: процесс просто не будет прибит автоматически.
                Log.Write("MAIN", "WARN job attach failed: " + ex.Message);
            }
        }

        private static IntPtr EnsureJob()
        {
            lock (_lock)
            {
                if (_initialized) return _job;
                _initialized = true;

                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    Log.Write("MAIN", "WARN CreateJobObject failed: win32=" + Marshal.GetLastWin32Error());
                    return IntPtr.Zero;
                }

                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
                    {
                        Log.Write("MAIN", "WARN SetInformationJobObject failed: win32=" + Marshal.GetLastWin32Error());
                        CloseHandle(job);
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                _job = job;
                return _job;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}

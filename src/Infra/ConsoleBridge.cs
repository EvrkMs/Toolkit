using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Toolkit
{
    /// <summary>
    /// Toolkit собран в оконной подсистеме (/target:winexe), чтобы автозагрузка
    /// не показывала чёрное окно консоли. Но при запуске из терминала вывод всё
    /// равно нужен, поэтому подключаемся к консоли родителя, если она есть.
    /// </summary>
    internal static class ConsoleBridge
    {
        private const int ATTACH_PARENT_PROCESS = -1;

        public static void AttachToParent()
        {
            try
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) return;

                StreamWriter stdout = new StreamWriter(Console.OpenStandardOutput());
                stdout.AutoFlush = true;
                Console.SetOut(stdout);

                StreamWriter stderr = new StreamWriter(Console.OpenStandardError());
                stderr.AutoFlush = true;
                Console.SetError(stderr);
            }
            catch
            {
                // Консоли нет — значит запустили из автозагрузки, писать некуда.
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
    }
}

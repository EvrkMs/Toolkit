using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Toolkit
{
    /// <summary>
    /// Единая точка перезапуска explorer на весь прогон. Шаги только заявляют
    /// потребность через Request(); фактический перезапуск происходит один раз.
    /// </summary>
    internal static class ExplorerRestart
    {
        public const string PolicyAuto = "auto";
        public const string PolicyNever = "never";
        public const string PolicyForce = "force";

        private static readonly List<string> _reasons = new List<string>();

        public static string Policy = PolicyAuto;
        public static bool Pending
        {
            get { return _reasons.Count > 0; }
        }

        public static void Request(string reason)
        {
            _reasons.Add(reason);
            Log.Write("EXPLORER", "restart requested: " + reason);
        }

        /// <summary>Возвращает true, если explorer был реально перезапущен.</summary>
        public static bool Commit()
        {
            bool force = string.Equals(Policy, PolicyForce, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(Policy, PolicyNever, StringComparison.OrdinalIgnoreCase))
            {
                if (_reasons.Count > 0)
                {
                    Log.Write("EXPLORER", "restart SKIPPED by policy=never (" + string.Join("; ", _reasons.ToArray()) + ")");
                }
                _reasons.Clear();
                return false;
            }

            if (!force && _reasons.Count == 0)
            {
                Log.Write("EXPLORER", "restart not needed");
                return false;
            }

            string why = force && _reasons.Count == 0 ? "policy=force" : string.Join("; ", _reasons.ToArray());
            _reasons.Clear();

            return Restart(why);
        }

        private static bool Restart(string why)
        {
            try
            {
                // Именно Kill, а не корректное завершение: при graceful-выходе Explorer
                // перезаписывает блоб Favorites своим состоянием из памяти, затирая
                // только что применённый профиль панели.
                Process[] processes = Process.GetProcessesByName("explorer");
                foreach (Process p in processes)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                    catch
                    {
                    }
                }

                Thread.Sleep(250);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.UseShellExecute = true;
                Process.Start(psi);

                bool ready = WaitForShell(15000);                Log.Write("EXPLORER", "restarted (" + why + ") shellReady=" + ready);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("EXPLORER", "WARN restart failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Ждём, пока поднимется оболочка, иначе следующий шаг (раскладка иконок)
        /// упрётся в ещё не существующий SysListView32.
        /// </summary>
        public static bool WaitForShell(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
                IntPtr progman = NativeMethods.FindWindow("Progman", null);
                if (tray != IntPtr.Zero && progman != IntPtr.Zero)
                {
                    return true;
                }

                Thread.Sleep(200);
                waited += 200;
            }

            return false;
        }
    }
}

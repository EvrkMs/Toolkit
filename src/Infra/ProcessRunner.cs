using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Toolkit
{
    internal static class ProcessRunner
    {
        public const int ExitTimeout = 124;

        public static int Run(string exe, string arguments, int timeoutSec)
        {
            string ignored;
            return Run(exe, arguments, timeoutSec, out ignored);
        }

        /// <summary>
        /// Скрытый запуск с чтением потоков через события: последовательный ReadToEnd
        /// на stdout+stderr способен взаимно заблокироваться, если дочерний процесс
        /// заполнит буфер stderr.
        /// </summary>
        public static int Run(string exe, string arguments, int timeoutSec, out string output)
        {
            StringBuilder buffer = new StringBuilder();
            output = "";

            ProcessStartInfo psi = BuildStartInfo(exe, arguments);

            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (buffer) { buffer.AppendLine(e.Data); } }
                };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (buffer) { buffer.AppendLine(e.Data); } }
                };

                p.Start();
                ChildProcessGuard.Attach(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                int timeoutMs = Math.Max(1, timeoutSec) * 1000;
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    lock (buffer) { output = buffer.ToString(); }
                    return ExitTimeout;
                }

                // Без второго WaitForExit() без аргументов асинхронные обработчики
                // могут не успеть дочитать хвост потоков.
                p.WaitForExit();
                lock (buffer) { output = buffer.ToString(); }
                return p.ExitCode;
            }
        }

        /// <summary>
        /// Для процессов, которые всё равно показывают окно (nvidiaProfileInspector):
        /// пока процесс жив, прячем любые его видимые окна.
        /// </summary>
        public static int RunAndSuppressWindows(string exe, string arguments, int timeoutSec)
        {
            ProcessStartInfo psi = BuildStartInfo(exe, arguments);

            using (Process p = Process.Start(psi))
            {
                if (p == null) return 1;
                ChildProcessGuard.Attach(p);

                DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, timeoutSec));
                while (!p.HasExited && DateTime.UtcNow < deadline)
                {
                    NativeMethods.HideProcessWindows(p.Id);
                    Thread.Sleep(50);
                }

                if (!p.HasExited)
                {
                    try { p.Kill(); } catch { }
                    return ExitTimeout;
                }

                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                return p.ExitCode;
            }
        }

        /// <summary>
        /// Квотирование по правилам CommandLineToArgvW: экранировать нужно кавычки
        /// и только те обратные слэши, что стоят вплотную перед кавычкой. Сплошная
        /// замена \ на \\ ломала пути — в аргументы уходило E:\\scripts\\tools.
        /// </summary>
        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";

            StringBuilder sb = new StringBuilder();
            sb.Append('"');

            int i = 0;
            while (i < value.Length)
            {
                int backslashes = 0;
                while (i < value.Length && value[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == value.Length)
                {
                    // Хвостовые слэши удваиваем, иначе они съедят закрывающую кавычку.
                    sb.Append('\\', backslashes * 2);
                    break;
                }

                if (value[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(value[i]);
                }

                i++;
            }

            sb.Append('"');
            return sb.ToString();
        }

        private static ProcessStartInfo BuildStartInfo(string exe, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = arguments ?? "";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            return psi;
        }
    }
}

using System;
using System.IO;
using System.Text;

namespace Toolkit
{
    internal static class Log
    {
        private static readonly object _lock = new object();
        private static string _path;
        private static bool _echo;

        /// <summary>
        /// Папка Toolkit общая для многих ПК, поэтому у каждого ПК свой файл: toolkit-ИМЯ_ПК.log.
        /// </summary>
        public static void Init(string logsDir, bool echoToConsole)
        {
            _path = Path.Combine(logsDir, "toolkit-" + SafeFileName(Environment.MachineName) + ".log");
            _echo = echoToConsole;

            try
            {
                Directory.CreateDirectory(logsDir);
            }
            catch
            {
                // Нет доступа к папке логов (например, диск ещё не примонтирован): пишем дальше без падения.
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "pc";

            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return name;
        }

        public static void Write(string component, string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " +
                          (component ?? "").PadRight(8) + " " + message;

            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Логирование никогда не должно ронять полезную работу.
                }
            }

            if (_echo)
            {
                Console.WriteLine(line);
            }
        }
    }
}

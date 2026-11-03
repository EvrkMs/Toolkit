using System;
using System.Diagnostics;
using System.Threading;

namespace Toolkit
{
    /// <summary>
    /// Условие готовности системы, которого шаги ждут перед стартом (драйвер видеокарты, оболочка и т.п.).
    /// Опрос выполняется один раз: тот, кто первым позвал Wait, опрашивает, остальные ждут его результата.
    /// Gate только задерживает шаг и никогда его не отменяет: если дождаться не удалось, шаг всё равно запускается
    /// и сам решает, что делать (так же вёл себя прежний код).
    /// </summary>
    internal abstract class Gate
    {
        private readonly object _sync = new object();
        private readonly ManualResetEvent _finished = new ManualResetEvent(false);
        private bool _started;
        private bool _ready;

        public abstract string Name { get; }

        /// <summary>Блокирующий опрос: true — условие выполнено, false — не дождались или ждать нечего.</summary>
        protected abstract bool Poll();

        /// <summary>Возвращает true, если система готова; false — если вышел таймаут или ждать было нечего.</summary>
        public bool Wait()
        {
            bool owner;
            lock (_sync)
            {
                owner = !_started;
                _started = true;
            }

            if (!owner)
            {
                _finished.WaitOne();
                return _ready;
            }

            Stopwatch watch = Stopwatch.StartNew();
            bool ok = false;
            try
            {
                ok = Poll();
            }
            catch (Exception ex)
            {
                Log.Write("GATE", Name + " ERROR " + ex.Message);
            }

            _ready = ok;
            Log.Write("GATE", Name + (ok ? " ready" : " NOT ready, идём дальше") +
                              " after " + (watch.ElapsedMilliseconds / 1000.0).ToString("0.#") + " c");
            _finished.Set();
            return ok;
        }

        /// <summary>Опрашивает условие каждые intervalMs, пока оно не выполнится или не истечёт timeoutMs.</summary>
        protected static bool PollUntil(Func<bool> condition, int timeoutMs, int intervalMs)
        {
            int waited = 0;
            while (true)
            {
                if (condition()) return true;
                if (waited >= timeoutMs) return false;

                Thread.Sleep(intervalMs);
                waited += intervalMs;
            }
        }
    }
}

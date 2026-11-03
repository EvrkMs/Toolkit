using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Toolkit
{
    /// <summary>Шаг загрузки: что выполнить, после каких шагов и какой готовности системы ждать.</summary>
    internal sealed class Step
    {
        public string Name;

        /// <summary>Имена шагов, которые должны завершиться раньше. Отсутствующие (выключенные) шаги игнорируются.</summary>
        public string[] Requires = new string[0];

        /// <summary>Готовность системы, которой шаг ждёт после своих Requires (драйвер, оболочка и т.п.).</summary>
        public Gate[] WaitFor = new Gate[0];

        /// <summary>Возвращает код выхода: 0 — успех.</summary>
        public Func<int> Action;
    }

    /// <summary>
    /// Выполняет шаги параллельно, соблюдая только заявленные зависимости: шаг стартует, когда завершились
    /// все его Requires и открылись все его WaitFor. Шаг без зависимостей стартует сразу.
    /// Сбой шага не останавливает остальные (как и раньше), итоговый код — худший из кодов шагов.
    /// </summary>
    internal static class Scheduler
    {
        private const string C = "SCHED";

        private sealed class Node
        {
            public Step Step;
            public Thread Thread;
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public int Result;
        }

        public static int Run(List<Step> steps)
        {
            Dictionary<string, Node> nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            List<Node> order = new List<Node>();
            List<string> names = new List<string>();

            foreach (Step step in steps)
            {
                if (nodes.ContainsKey(step.Name))
                {
                    throw new InvalidOperationException("duplicate step: " + step.Name);
                }

                Node node = new Node();
                node.Step = step;
                nodes[step.Name] = node;
                order.Add(node);
                names.Add(step.Name);
            }

            EnsureAcyclic(nodes);

            Stopwatch total = Stopwatch.StartNew();
            Log.Write(C, "steps: " + string.Join(", ", names.ToArray()));

            // Поток на шаг: шаги блокируются на ожидании драйвера, и пул потоков на это не рассчитан.
            foreach (Node node in order)
            {
                Node captured = node;
                captured.Thread = new Thread(delegate() { Execute(captured, nodes); });
                captured.Thread.IsBackground = true;
                captured.Thread.Name = "step:" + captured.Step.Name;
            }

            foreach (Node node in order)
            {
                node.Thread.Start();
            }

            int worst = 0;
            foreach (Node node in order)
            {
                node.Done.WaitOne();
                worst = Math.Max(worst, node.Result);
            }

            Log.Write(C, "all steps finished worst=" + worst + " in " + Seconds(total));
            return worst;
        }

        private static void Execute(Node node, Dictionary<string, Node> nodes)
        {
            Step step = node.Step;
            Stopwatch waiting = Stopwatch.StartNew();

            try
            {
                foreach (string required in step.Requires)
                {
                    Node dependency;
                    if (nodes.TryGetValue(required, out dependency))
                    {
                        dependency.Done.WaitOne();
                    }
                }

                foreach (Gate gate in step.WaitFor)
                {
                    gate.Wait();
                }

                Log.Write(C, step.Name + " start (ждал " + Seconds(waiting) + ")");

                Stopwatch running = Stopwatch.StartNew();
                node.Result = step.Action();

                Log.Write(C, step.Name + " done code=" + node.Result + " за " + Seconds(running));
            }
            catch (Exception ex)
            {
                node.Result = ExitCode.Failed;
                Log.Write(C, step.Name + " ERROR " + ex.Message);
            }
            finally
            {
                node.Done.Set();
            }
        }

        /// <summary>Цикл в зависимостях привёл бы к вечному ожиданию на старте, поэтому проверяем заранее.</summary>
        private static void EnsureAcyclic(Dictionary<string, Node> nodes)
        {
            Dictionary<string, int> state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in nodes.Keys)
            {
                Visit(name, nodes, state);
            }
        }

        private static void Visit(string name, Dictionary<string, Node> nodes, Dictionary<string, int> state)
        {
            int current;
            state.TryGetValue(name, out current);

            if (current == 2) return;
            if (current == 1) throw new InvalidOperationException("cyclic step dependency at: " + name);

            state[name] = 1;
            foreach (string required in nodes[name].Step.Requires)
            {
                if (nodes.ContainsKey(required)) Visit(required, nodes, state);
            }
            state[name] = 2;
        }

        private static string Seconds(Stopwatch watch)
        {
            return (watch.ElapsedMilliseconds / 1000.0).ToString("0.#") + " c";
        }
    }
}

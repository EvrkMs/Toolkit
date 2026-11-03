using System;

namespace Toolkit
{
    internal static class RunVerb
    {
        public static int Run(ToolkitConfig cfg)
        {
            int worst = 0;

            // Первым и синхронно: GameFix.bat убивает explorer уже через ~10 с после входа.
            worst = Math.Max(worst, Safe("GUARD", delegate { return GuardVerb.Ensure(cfg); }));

            // Остальное — граф шагов. Каждый шаг ждёт только то, что сам объявил (Requires — другие шаги,
            // WaitFor — готовность системы, например драйвер NVIDIA), остальные идут параллельно.
            // Состав шагов и их зависимости: StepCatalog.
            worst = Math.Max(worst, Scheduler.Run(StepCatalog.Build(cfg)));

            return worst;
        }

        private static int Safe(string component, Func<int> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Log.Write(component, "ERROR " + ex.Message);
                return ExitCode.Failed;
            }
        }
    }
}

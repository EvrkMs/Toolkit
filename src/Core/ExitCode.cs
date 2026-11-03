namespace Toolkit
{
    /// <summary>Коды выхода шагов и процесса. Раньше были числами 0/1/2/10, разбросанными по коду.</summary>
    internal static class ExitCode
    {
        /// <summary>Успех (в том числе «шаг выключен» или «делать нечего»).</summary>
        public const int Ok = 0;

        /// <summary>Фатальная ошибка запуска (например, не прочитался config.json).</summary>
        public const int Fatal = 1;

        /// <summary>Неверное использование командной строки.</summary>
        public const int Usage = 2;

        /// <summary>Шаг не справился, остальные продолжают работу.</summary>
        public const int Failed = 10;
    }
}

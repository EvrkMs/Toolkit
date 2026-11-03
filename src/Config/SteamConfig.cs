using System;
using System.Runtime.Serialization;

namespace Toolkit
{
    [DataContract]
    internal sealed class SteamConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>Папка Steam; пусто — из реестра (HKLM\...\Valve\Steam\InstallPath).</summary>
        [DataMember(Name = "installPath")]
        public string InstallPath { get; set; }

        /// <summary>Общая папка на игровом диске, куда ведут ссылки.</summary>
        [DataMember(Name = "source")]
        public string Source { get; set; }

        /// <summary>Сколько секунд ждать появления source после загрузки.</summary>
        [DataMember(Name = "waitSeconds")]
        public int WaitSeconds { get; set; }

        /// <summary>В run после ссылок докачать .ico установленных игр (то же, что steam icons).</summary>
        [DataMember(Name = "downloadIcons")]
        public bool DownloadIcons { get; set; }

        [DataMember(Name = "links")]
        public SteamLink[] Links { get; set; }
    }

    [DataContract]
    internal sealed class SteamLink
    {
        /// <summary>Путь внутри папки Steam, например steam\games.</summary>
        [DataMember(Name = "path")]
        public string Path { get; set; }

        /// <summary>Подпапка в source, на которую ведёт ссылка.</summary>
        [DataMember(Name = "source")]
        public string Source { get; set; }
    }
}

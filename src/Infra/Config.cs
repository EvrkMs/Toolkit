using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Toolkit
{
    internal static class Paths
    {
        public static string BaseDir;

        public static string ProfilesDir
        {
            get { return Path.Combine(BaseDir, "profiles"); }
        }

        public static string TaskbarProfile
        {
            get { return Path.Combine(ProfilesDir, "taskbar.json"); }
        }

        public static string ShortcutProfilesDir
        {
            get { return Path.Combine(ProfilesDir, "shortcuts"); }
        }

        public static string RegFilesDir
        {
            get { return Path.Combine(BaseDir, "reg"); }
        }

        public static string DefaultConfig
        {
            get { return Path.Combine(BaseDir, "config.json"); }
        }

        /// <summary>Разворачивает %VAR% и делает относительные пути относительными к Toolkit.</summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (Path.IsPathRooted(expanded)) return expanded;

            return Path.GetFullPath(Path.Combine(BaseDir, expanded));
        }
    }

    internal static class ConfigLoader
    {
        public static ToolkitConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("config not found: " + path, path);
            }

            using (FileStream fs = File.OpenRead(path))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(ToolkitConfig));
                ToolkitConfig cfg = (ToolkitConfig)ser.ReadObject(fs);
                if (cfg == null) throw new InvalidDataException("config parse failed: " + path);
                cfg.ApplyDefaults();
                return cfg;
            }
        }
    }

    [DataContract]
    internal sealed class ToolkitConfig
    {
        [DataMember(Name = "explorer")]
        public ExplorerConfig Explorer { get; set; }

        [DataMember(Name = "reg")]
        public RegConfig Reg { get; set; }

        [DataMember(Name = "nvidia")]
        public NvidiaConfig Nvidia { get; set; }

        [DataMember(Name = "resolutions")]
        public ResolutionsConfig Resolutions { get; set; }

        [DataMember(Name = "scaling")]
        public ScalingConfig Scaling { get; set; }

        [DataMember(Name = "audio")]
        public AudioConfig Audio { get; set; }

        [DataMember(Name = "symlink")]
        public SymlinkConfig Symlink { get; set; }

        [DataMember(Name = "steam")]
        public SteamConfig Steam { get; set; }

        [DataMember(Name = "taskbar")]
        public StepConfig Taskbar { get; set; }

        [DataMember(Name = "shortcuts")]
        public StepConfig Shortcuts { get; set; }

        /// <summary>Сколько шаги ждут готовности системы (драйвер видеокарты, NVIDIA-аудио, оболочка).</summary>
        [DataMember(Name = "gates")]
        public GatesConfig Gates { get; set; }

        /// <summary>Не даёт GameFix.bat убивать explorer.exe через taskkill (см. GuardVerb).</summary>
        [DataMember(Name = "guard")]
        public StepConfig Guard { get; set; }

        public void ApplyDefaults()
        {
            if (Explorer == null) Explorer = new ExplorerConfig();
            if (Reg == null) Reg = new RegConfig();
            if (Nvidia == null) Nvidia = new NvidiaConfig();
            if (Resolutions == null) Resolutions = new ResolutionsConfig();
            if (Scaling == null) Scaling = new ScalingConfig();
            if (Audio == null) Audio = new AudioConfig();
            if (Symlink == null) Symlink = new SymlinkConfig();
            if (Steam == null) Steam = new SteamConfig();
            if (Steam.WaitSeconds <= 0) Steam.WaitSeconds = 60;
            if (string.IsNullOrWhiteSpace(Steam.Source)) Steam.Source = @"data\GameFix\Steam";
            if (Steam.Links == null || Steam.Links.Length == 0)
            {
                Steam.Links = new[]
                {
                    new SteamLink { Path = "appcache", Source = "appcache" },
                    new SteamLink { Path = "depotcache", Source = "depotcache" },
                    new SteamLink { Path = @"steam\games", Source = "steam_games" }
                };
            }
            if (Taskbar == null) Taskbar = new StepConfig();
            if (Shortcuts == null) Shortcuts = new StepConfig();
            if (Guard == null) Guard = new StepConfig();
            if (Gates == null) Gates = new GatesConfig();
            Gates.ApplyDefaults();

            if (string.IsNullOrWhiteSpace(Explorer.RestartPolicy))
            {
                Explorer.RestartPolicy = ExplorerRestart.PolicyAuto;
            }

            if (Nvidia.TimeoutSeconds <= 0) Nvidia.TimeoutSeconds = 20;
            if (Symlink.TimeoutSeconds <= 0) Symlink.TimeoutSeconds = 180;
            if (string.IsNullOrWhiteSpace(Symlink.Root)) Symlink.Root = @"data\GameFix";
        }
    }

    [DataContract]
    internal sealed class ExplorerConfig
    {
        [DataMember(Name = "restartPolicy")]
        public string RestartPolicy { get; set; }    }

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

    /// <summary>
    /// Таймауты ожидания готовности системы. Gate только задерживает шаг: по таймауту шаг всё равно запускается.
    /// </summary>
    [DataContract]
    internal sealed class GatesConfig
    {
        /// <summary>Сколько секунд ждать, пока дисплей перейдёт на драйвер NVIDIA. Не задано — 60.</summary>
        [DataMember(Name = "gpuDriverSeconds")]
        public int? GpuDriverSeconds { get; set; }

        /// <summary>Сколько секунд ждать оболочку Windows перед раскладкой иконок. Не задано — 15.</summary>
        [DataMember(Name = "shellSeconds")]
        public int? ShellSeconds { get; set; }

        public void ApplyDefaults()
        {
            GpuDriverSeconds = Clamp(GpuDriverSeconds, 60);
            ShellSeconds = Clamp(ShellSeconds, 15);
        }

        private static int Clamp(int? value, int fallback)
        {
            if (!value.HasValue) return fallback;
            return value.Value < 0 ? 0 : value.Value;
        }
    }

    [DataContract]
    internal sealed class StepConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }
    }

    [DataContract]
    internal sealed class RegConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>Дополнительные .reg сверх тех, что лежат в Toolkit\reg.</summary>
        [DataMember(Name = "files")]
        public string[] Files { get; set; }
    }

    [DataContract]
    internal sealed class NvidiaConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "exePath")]
        public string ExePath { get; set; }

        [DataMember(Name = "profilePath")]
        public string ProfilePath { get; set; }

        [DataMember(Name = "arguments")]
        public string Arguments { get; set; }

        [DataMember(Name = "timeoutSeconds")]
        public int TimeoutSeconds { get; set; }
    }

    [DataContract]
    internal sealed class ResolutionsConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>\\.\DISPLAYn; пусто — основной монитор.</summary>
        [DataMember(Name = "display")]
        public string Display { get; set; }

        /// <summary>native — масштабирует видеокарта (по умолчанию); real — монитор получает сигнал WxH.</summary>
        [DataMember(Name = "timing")]
        public string Timing { get; set; }

        /// <summary>auto|edid|dmt|dmt-rb|cvt|cvt-rb|gtf</summary>
        [DataMember(Name = "timingStandard")]
        public string TimingStandard { get; set; }

        [DataMember(Name = "modes")]
        public ResolutionMode[] Modes { get; set; }
    }

    [DataContract]
    internal sealed class ResolutionMode
    {
        [DataMember(Name = "width")]
        public int Width { get; set; }

        [DataMember(Name = "height")]
        public int Height { get; set; }

        /// <summary>Гц; 0 или не задано — текущая частота.</summary>
        [DataMember(Name = "refresh")]
        public float Refresh { get; set; }
    }

    [DataContract]
    internal sealed class ScalingConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>\\.\DISPLAYn; пусто — основной монитор.</summary>
        [DataMember(Name = "display")]
        public string Display { get; set; }

        /// <summary>fullscreen|aspect|none|integer; масштабирование всегда на ГП.</summary>
        [DataMember(Name = "mode")]
        public string Mode { get; set; }
    }

    [DataContract]
    internal sealed class AudioConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "speakerVolume")]
        public int SpeakerVolume { get; set; }

        [DataMember(Name = "microphoneVolume")]
        public int MicrophoneVolume { get; set; }

        [DataMember(Name = "speakerDeviceId")]
        public string SpeakerDeviceId { get; set; }

        [DataMember(Name = "microphoneDeviceId")]
        public string MicrophoneDeviceId { get; set; }

        [DataMember(Name = "disableBoost")]
        public bool? DisableBoost { get; set; }

        [DataMember(Name = "disableAgc")]
        public bool? DisableAgc { get; set; }

        /// <summary>true — отключить все не-USB звуковые устройства (в т.ч. NVIDIA HDMI), если USB-устройство есть. Не задано = false.</summary>
        [DataMember(Name = "usbOnly")]
        public bool? UsbOnly { get; set; }
    }

    [DataContract]
    internal sealed class SymlinkConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "root")]
        public string Root { get; set; }

        [DataMember(Name = "recreate")]
        public bool? Recreate { get; set; }

        [DataMember(Name = "includeProgramData")]
        public bool? IncludeProgramData { get; set; }

        [DataMember(Name = "includeProgramFiles")]
        public bool? IncludeProgramFiles { get; set; }

        [DataMember(Name = "timeoutSeconds")]
        public int TimeoutSeconds { get; set; }
    }
}

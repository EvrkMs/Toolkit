using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Toolkit
{
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
}

using System;
using System.Runtime.Serialization;

namespace Toolkit
{
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
}

using System;
using System.Runtime.Serialization;

namespace Toolkit
{
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
}

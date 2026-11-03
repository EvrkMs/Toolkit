using System;
using System.Runtime.Serialization;

namespace Toolkit
{
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
}

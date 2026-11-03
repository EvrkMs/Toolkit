using System;
using System.Runtime.Serialization;

namespace Toolkit
{
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

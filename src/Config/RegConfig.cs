using System;
using System.Runtime.Serialization;

namespace Toolkit
{
    [DataContract]
    internal sealed class RegConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>Дополнительные .reg сверх тех, что лежат в Toolkit\reg.</summary>
        [DataMember(Name = "files")]
        public string[] Files { get; set; }
    }
}

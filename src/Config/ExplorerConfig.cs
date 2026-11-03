using System;
using System.Runtime.Serialization;

namespace Toolkit
{
    [DataContract]
    internal sealed class ExplorerConfig
    {
        [DataMember(Name = "restartPolicy")]
        public string RestartPolicy { get; set; }
    }
}

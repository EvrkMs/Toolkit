using System;
using System.Runtime.Serialization;

namespace Toolkit
{
    [DataContract]
    internal sealed class StepConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }
    }
}

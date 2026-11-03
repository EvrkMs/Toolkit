using System;
using System.Runtime.Serialization;

namespace Toolkit
{
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
}

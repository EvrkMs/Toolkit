using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Toolkit
{
    internal static partial class AudioVerb
    {
        private const int CLSCTX_ALL = 23;
        private const int STGM_READWRITE = 0x2;
        private const uint ENDPOINT_SYSFX_DISABLED = 0x00000001;
        private const uint DEVICE_STATE_ACTIVE = 0x00000001;
        private const uint DEVICE_STATE_UNPLUGGED = 0x00000008;
        private const uint DEVICE_STATE_ALL = 0x0000000F;

        private static readonly Guid PKEY_AudioEndpoint_Disable_SysFx_Fmtid =
            new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e");
        private const int PKEY_AudioEndpoint_Disable_SysFx_Pid = 5;

        private static readonly Guid KSAUDFNAME_MICROPHONE_BOOST =
            new Guid("2BC31D6A-96E3-11D2-AC4C-00C04F8EFB68");

        private sealed class EndpointRef
        {
            public IMMDevice Device;
            public string Id;
            public int State;
            public string Source;
        }

        /// <summary>Команда audio целиком: сначала устройства, потом уровни.</summary>
        public static int Run(ToolkitConfig cfg)
        {
            if (!cfg.Audio.Enabled)
            {
                Log.Write("AUDIO", "SKIP disabled");
                return 0;
            }

            int devices = RunDevices(cfg);
            int levels = RunLevels(cfg);
            return Math.Max(devices, levels);
        }

        /// <summary>
        /// Шаг audio.devices: отключает все устройства ВЫВОДА, которые не USB (встроенный Realtek, HDMI/DP NVIDIA,
        /// Bluetooth), независимо от того, подключено ли USB-устройство. Если USB-вывод есть, он становится
        /// устройством по умолчанию; если нет, Windows сама назначит его при подключении, потому что других не осталось.
        /// Устройства ввода (микрофоны) не отключаются никогда.
        /// Шаг должен выполняться после загрузки драйвера NVIDIA (gate GpuDriver).
        /// </summary>
        public static int RunDevices(ToolkitConfig cfg)
        {
            AudioConfig audio = cfg.Audio;

            if (!audio.Enabled || !audio.UsbOnly.HasValue || !audio.UsbOnly.Value)
            {
                Log.Write("AUDIO", "devices SKIP usbOnly выключен");
                return 0;
            }

            try
            {
                ApplyUsbOnly(EDataFlow.eRender, "out");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("AUDIO", "devices ERROR " + ex.Message);
                return ExitCode.Failed;
            }
        }

        /// <summary>Шаг audio.levels: громкость колонок и микрофона, AGC, усиление микрофона. Ничего не отключает.</summary>
        public static int RunLevels(ToolkitConfig cfg)
        {
            AudioConfig audio = cfg.Audio;

            if (!audio.Enabled)
            {
                Log.Write("AUDIO", "SKIP disabled");
                return 0;
            }

            try
            {
                if (audio.SpeakerVolume >= 0)
                {
                    EndpointRef speaker = ResolvePreferredEndpoint(audio.SpeakerDeviceId, EDataFlow.eRender, false);
                    if (speaker == null)
                    {
                        Log.Write("AUDIO", "speaker FAIL no-endpoint");
                    }
                    else
                    {
                        int hr;
                        bool ok = SetEndpointVolume(speaker.Device, audio.SpeakerVolume, out hr);
                        Log.Write("AUDIO", ok
                            ? "speaker OK " + audio.SpeakerVolume + "% id=" + speaker.Id
                            : "speaker FAIL hr=" + Hr(hr) + " id=" + speaker.Id);
                    }
                }

                List<EndpointRef> captureTargets = ResolveCaptureTargets(audio.MicrophoneDeviceId);
                EndpointRef primaryCapture = FindPrimaryActive(captureTargets);

                if (audio.MicrophoneVolume >= 0)
                {
                    if (primaryCapture == null)
                    {
                        Log.Write("AUDIO", "mic FAIL no-capture-endpoint");
                    }
                    else
                    {
                        int hr;
                        bool ok = SetEndpointVolume(primaryCapture.Device, audio.MicrophoneVolume, out hr);
                        Log.Write("AUDIO", ok
                            ? "mic OK " + audio.MicrophoneVolume + "% id=" + primaryCapture.Id + " source=" + primaryCapture.Source
                            : "mic FAIL hr=" + Hr(hr) + " id=" + primaryCapture.Id + " source=" + primaryCapture.Source);
                    }
                }

                if (!audio.DisableAgc.HasValue || audio.DisableAgc.Value)
                {
                    ApplyAgcDisable(captureTargets);
                }

                if (!audio.DisableBoost.HasValue || audio.DisableBoost.Value)
                {
                    ApplyBoostDisable(captureTargets);
                }

                Log.Write("AUDIO", "DONE");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("AUDIO", "ERROR " + ex.Message);
                return ExitCode.Failed;
            }
        }

        /// <summary>
        /// Все не-USB устройства направления (HDMI/NVIDIA, встроенное, Bluetooth) отключаются всегда,
        /// есть USB-устройство или нет. Если активное USB-устройство есть, оно становится устройством
        /// по умолчанию; если нет, других устройств не осталось, и Windows сама назначит USB при подключении.
        /// </summary>
        private static void ApplyUsbOnly(EDataFlow flow, string label)
        {
            List<EndpointRef> usb = new List<EndpointRef>();
            List<EndpointRef> others = new List<EndpointRef>();

            // active + unplugged: неподключённые гнёзда встроенной карты тоже убираем.
            foreach (EndpointRef endpoint in EnumerateEndpoints(flow, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED, "usb-scan"))
            {
                if (IsUsbEndpoint(endpoint.Device)) usb.Add(endpoint);
                else others.Add(endpoint);
            }

            bool usbActive = false;
            foreach (EndpointRef endpoint in usb)
            {
                if ((endpoint.State & (int)DEVICE_STATE_ACTIVE) != 0) usbActive = true;
            }

            if (others.Count == 0 && !usbActive)
            {
                Log.Write("AUDIO", "usbOnly " + label + " nothing to do: устройств нет");
                return;
            }

            IPolicyConfig policy;
            try
            {
                policy = (IPolicyConfig)(new PolicyConfigClient());
            }
            catch (Exception ex)
            {
                Log.Write("AUDIO", "usbOnly " + label + " FAIL policy config: " + ex.Message);
                return;
            }

            // Сначала USB по умолчанию: иначе система переключит дефолт на случайное устройство.
            // Без активного USB дефолт не трогаем.
            EndpointRef primaryUsb = usbActive ? FindPrimaryActive(usb) : null;
            if (primaryUsb != null)
            {
                foreach (ERole role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
                {
                    int hrDefault = policy.SetDefaultEndpoint(primaryUsb.Id, role);
                    if (hrDefault != 0) Log.Write("AUDIO", "usbOnly " + label + " default " + role + " FAIL hr=" + Hr(hrDefault));
                }
            }

            int disabled = 0;
            foreach (EndpointRef endpoint in others)
            {
                int hr = policy.SetEndpointVisibility(endpoint.Id, 0);
                Log.Write("AUDIO", "usbOnly " + label + " disable " + (hr == 0 ? "OK" : "FAIL hr=" + Hr(hr)) +
                                   " name=" + GetEndpointName(endpoint.Device) + " id=" + endpoint.Id);
                if (hr == 0) disabled++;
            }

            Log.Write("AUDIO", "usbOnly " + label + " usb=" + usb.Count + " disabled=" + disabled + "/" + others.Count +
                               " default=" + (primaryUsb == null ? "<не менялся, USB нет>" : GetEndpointName(primaryUsb.Device)));
        }

        private static void ApplyAgcDisable(List<EndpointRef> captureTargets)
        {
            if (captureTargets.Count == 0)
            {
                Log.Write("AUDIO", "agc OFF no-capture-endpoints");
                return;
            }

            int sysFxChanged = 0;
            int controlsDisabled = 0;

            foreach (EndpointRef endpoint in captureTargets)
            {
                int hrStore, hrSet, hrCommit;
                if (DisableSystemEffects(endpoint.Device, out hrStore, out hrSet, out hrCommit)) sysFxChanged++;

                TopologyStats stats;
                int changed = TraverseTopologyParts(endpoint.Device, DisableAgcOnPart, out stats);
                controlsDisabled += changed;

                Log.Write("AUDIO", "agc endpoint id=" + endpoint.Id +
                                   " source=" + endpoint.Source +
                                   " state=" + StateToString(endpoint.State) +
                                   " sysfxHr=" + Hr(hrCommit) +
                                   " topo=" + Hr(stats.TopologyHr) +
                                   " parts=" + stats.PartsScanned +
                                   " controls=" + changed);
            }

            Log.Write("AUDIO", "agc OFF endpoints=" + captureTargets.Count +
                               " sysfx=" + sysFxChanged + " controls=" + controlsDisabled);
        }

        private static void ApplyBoostDisable(List<EndpointRef> captureTargets)
        {
            if (captureTargets.Count == 0)
            {
                Log.Write("AUDIO", "boost OFF no-capture-endpoints");
                return;
            }

            int controlsDisabled = 0;

            foreach (EndpointRef endpoint in captureTargets)
            {
                TopologyStats stats;
                int changed = TraverseTopologyParts(endpoint.Device, DisableBoostOnPart, out stats);
                controlsDisabled += changed;

                Log.Write("AUDIO", "boost endpoint id=" + endpoint.Id +
                                   " source=" + endpoint.Source +
                                   " topo=" + Hr(stats.TopologyHr) +
                                   " parts=" + stats.PartsScanned +
                                   " controls=" + changed);
            }

            Log.Write("AUDIO", "boost OFF endpoints=" + captureTargets.Count + " controls=" + controlsDisabled);
        }
    }
}

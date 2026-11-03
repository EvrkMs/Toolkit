using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Toolkit
{
    internal static class AudioVerb
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
                return 10;
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
                return 10;
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

        /// <summary>USB, если коннектор конечной точки ведёт на адаптер с путём usb#vid_...</summary>
        private static bool IsUsbEndpoint(IMMDevice device)
        {
            if (device == null) return false;

            Guid topoIid = typeof(IDeviceTopology).GUID;
            object topoObj;
            if (device.Activate(ref topoIid, CLSCTX_ALL, IntPtr.Zero, out topoObj) != 0 || topoObj == null) return false;

            IDeviceTopology topology = topoObj as IDeviceTopology;
            if (topology == null) return false;

            uint connectorCount;
            if (topology.GetConnectorCount(out connectorCount) != 0) return false;

            for (uint i = 0; i < connectorCount; i++)
            {
                IConnector connector;
                if (topology.GetConnector(i, out connector) != 0 || connector == null) continue;

                string adapterId;
                if (connector.GetDeviceIdConnectedTo(out adapterId) != 0 || string.IsNullOrWhiteSpace(adapterId)) continue;

                if (adapterId.IndexOf("usb#", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private static string GetEndpointName(IMMDevice device)
        {
            if (device == null) return "<null>";

            IPropertyStore store;
            if (device.OpenPropertyStore(0, out store) != 0 || store == null) return "<unknown>";

            // PKEY_Device_FriendlyName {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
            PROPERTYKEY key = new PROPERTYKEY();
            key.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
            key.pid = 14;

            PROPVARIANT value;
            if (store.GetValue(ref key, out value) != 0) return "<unknown>";

            try
            {
                // VT_LPWSTR = 31; указатель лежит по смещению 8.
                if (value.vt != 31 || value.ptr == IntPtr.Zero) return "<unknown>";
                return Marshal.PtrToStringUni(value.ptr);
            }
            finally
            {
                Ole32.PropVariantClear(ref value);
            }
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

        private static EndpointRef FindPrimaryActive(List<EndpointRef> endpoints)
        {
            if (endpoints.Count == 0) return null;

            foreach (EndpointRef endpoint in endpoints)
            {
                if ((endpoint.State & (int)DEVICE_STATE_ACTIVE) != 0) return endpoint;
            }

            return endpoints[0];
        }

        private static EndpointRef ResolvePreferredEndpoint(string endpointId, EDataFlow flow, bool includeInactiveFallback)
        {
            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                IMMDevice explicitDevice;
                int hr = TryGetDeviceById(endpointId, out explicitDevice);
                if (hr == 0 && explicitDevice != null)
                {
                    return CreateRef(explicitDevice, GetDeviceId(explicitDevice), GetDeviceState(explicitDevice), "explicit");
                }

                Log.Write("AUDIO", "explicit endpoint lookup FAIL id=" + endpointId + " hr=" + Hr(hr));
            }

            ERole[] roles = new[] { ERole.eMultimedia, ERole.eCommunications, ERole.eConsole };
            foreach (ERole role in roles)
            {
                IMMDevice device;
                if (TryGetDefaultEndpoint(flow, role, out device) == 0 && device != null)
                {
                    return CreateRef(device, GetDeviceId(device), GetDeviceState(device), "default-" + role);
                }
            }

            uint mask = includeInactiveFallback ? DEVICE_STATE_ALL : DEVICE_STATE_ACTIVE;
            List<EndpointRef> enumerated = EnumerateEndpoints(flow, mask, "enum");
            return enumerated.Count > 0 ? enumerated[0] : null;
        }

        private static List<EndpointRef> ResolveCaptureTargets(string endpointId)
        {
            List<EndpointRef> result = new List<EndpointRef>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                IMMDevice explicitDevice;
                int hr = TryGetDeviceById(endpointId, out explicitDevice);
                if (hr == 0 && explicitDevice != null)
                {
                    AddIfNew(result, seen, CreateRef(explicitDevice, GetDeviceId(explicitDevice), GetDeviceState(explicitDevice), "explicit"));
                    return result;
                }

                Log.Write("AUDIO", "capture explicit lookup FAIL id=" + endpointId + " hr=" + Hr(hr));
                return result;
            }

            ERole[] roles = new[] { ERole.eMultimedia, ERole.eCommunications, ERole.eConsole };
            foreach (ERole role in roles)
            {
                IMMDevice device;
                if (TryGetDefaultEndpoint(EDataFlow.eCapture, role, out device) == 0 && device != null)
                {
                    AddIfNew(result, seen, CreateRef(device, GetDeviceId(device), GetDeviceState(device), "default-" + role));
                }
            }

            foreach (EndpointRef endpoint in EnumerateEndpoints(EDataFlow.eCapture, DEVICE_STATE_ACTIVE, "enum-active"))
            {
                AddIfNew(result, seen, endpoint);
            }

            if (result.Count == 0)
            {
                foreach (EndpointRef endpoint in EnumerateEndpoints(EDataFlow.eCapture, DEVICE_STATE_ALL, "enum-all"))
                {
                    AddIfNew(result, seen, endpoint);
                }
            }

            Log.Write("AUDIO", "capture endpoints: " + result.Count);
            return result;
        }

        private static void AddIfNew(List<EndpointRef> list, HashSet<string> seen, EndpointRef endpoint)
        {
            if (endpoint == null || endpoint.Device == null) return;

            string key = string.IsNullOrWhiteSpace(endpoint.Id) ? Guid.NewGuid().ToString("N") : endpoint.Id;
            if (seen.Add(key)) list.Add(endpoint);
        }

        private static EndpointRef CreateRef(IMMDevice device, string id, int state, string source)
        {
            EndpointRef endpoint = new EndpointRef();
            endpoint.Device = device;
            endpoint.Id = id;
            endpoint.State = state;
            endpoint.Source = source;
            return endpoint;
        }

        private static List<EndpointRef> EnumerateEndpoints(EDataFlow flow, uint stateMask, string source)
        {
            List<EndpointRef> result = new List<EndpointRef>();
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());

            IMMDeviceCollection collection;
            int hr = enumerator.EnumAudioEndpoints(flow, stateMask, out collection);
            if (hr != 0 || collection == null)
            {
                Log.Write("AUDIO", "enum FAIL flow=" + flow + " mask=" + stateMask + " hr=" + Hr(hr));
                return result;
            }

            uint count;
            if (collection.GetCount(out count) != 0) return result;

            for (uint i = 0; i < count; i++)
            {
                IMMDevice device;
                if (collection.Item(i, out device) != 0 || device == null) continue;
                result.Add(CreateRef(device, GetDeviceId(device), GetDeviceState(device), source));
            }

            return result;
        }

        private static int TryGetDeviceById(string endpointId, out IMMDevice device)
        {
            device = null;
            if (string.IsNullOrWhiteSpace(endpointId)) return unchecked((int)0x80070057);

            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
            return enumerator.GetDevice(endpointId, out device);
        }

        private static int TryGetDefaultEndpoint(EDataFlow flow, ERole role, out IMMDevice device)
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
            return enumerator.GetDefaultAudioEndpoint(flow, role, out device);
        }

        private static int GetDeviceState(IMMDevice device)
        {
            if (device == null) return 0;

            int state;
            return device.GetState(out state) == 0 ? state : 0;
        }

        private static string GetDeviceId(IMMDevice device)
        {
            if (device == null) return "";

            string id;
            if (device.GetId(out id) != 0 || string.IsNullOrWhiteSpace(id)) return "";
            return id;
        }

        private static bool SetEndpointVolume(IMMDevice device, int volumePercent, out int hrResult)
        {
            hrResult = 0;
            if (device == null)
            {
                hrResult = unchecked((int)0x80070490);
                return false;
            }

            volumePercent = Math.Max(0, Math.Min(100, volumePercent));
            float scalar = volumePercent / 100f;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object epObj;
            int hrActivate = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out epObj);
            if (hrActivate != 0 || epObj == null)
            {
                hrResult = hrActivate != 0 ? hrActivate : unchecked((int)0x80004003);
                return false;
            }

            IAudioEndpointVolume ep = epObj as IAudioEndpointVolume;
            if (ep == null)
            {
                hrResult = unchecked((int)0x80004002);
                return false;
            }

            Guid ctx = Guid.Empty;
            hrResult = ep.SetMasterVolumeLevelScalar(scalar, ref ctx);
            return hrResult == 0;
        }

        private static bool DisableSystemEffects(IMMDevice device, out int hrStore, out int hrSet, out int hrCommit)
        {
            hrStore = 0;
            hrSet = 0;
            hrCommit = 0;

            if (device == null)
            {
                hrStore = unchecked((int)0x80070490);
                return false;
            }

            IPropertyStore store;
            hrStore = device.OpenPropertyStore(STGM_READWRITE, out store);
            if (hrStore != 0 || store == null) return false;

            PROPERTYKEY key = new PROPERTYKEY();
            key.fmtid = PKEY_AudioEndpoint_Disable_SysFx_Fmtid;
            key.pid = PKEY_AudioEndpoint_Disable_SysFx_Pid;

            PROPVARIANT value = PROPVARIANT.FromUInt32(ENDPOINT_SYSFX_DISABLED);
            try
            {
                hrSet = store.SetValue(ref key, ref value);
                if (hrSet != 0) return false;

                hrCommit = store.Commit();
                return hrCommit == 0;
            }
            finally
            {
                Ole32.PropVariantClear(ref value);
            }
        }

        private struct TopologyStats
        {
            public int PartsScanned;
            public int TopologyHr;
            public int SeedParts;
            public bool UsedFallback;
        }

        private static int TraverseTopologyParts(IMMDevice device, Func<IPart, bool> action, out TopologyStats stats)
        {
            stats = new TopologyStats();

            if (device == null)
            {
                stats.TopologyHr = unchecked((int)0x80070490);
                return 0;
            }

            Guid topoIid = typeof(IDeviceTopology).GUID;
            object topoObj;
            stats.TopologyHr = device.Activate(ref topoIid, CLSCTX_ALL, IntPtr.Zero, out topoObj);
            if (stats.TopologyHr != 0 || topoObj == null) return 0;

            IDeviceTopology topology = topoObj as IDeviceTopology;
            if (topology == null)
            {
                stats.TopologyHr = unchecked((int)0x80004002);
                return 0;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<IPart> stack = new Stack<IPart>();

            uint connectorCount;
            uint subunitCount;
            topology.GetConnectorCount(out connectorCount);
            topology.GetSubunitCount(out subunitCount);

            for (uint i = 0; i < connectorCount; i++)
            {
                IConnector connector;
                if (topology.GetConnector(i, out connector) != 0 || connector == null) continue;

                IPart connectorPart;
                if (TryCastToPart(connector, out connectorPart) && Enqueue(connectorPart, stack, visited))
                {
                    stats.SeedParts++;
                }

                IConnector connected;
                if (connector.GetConnectedTo(out connected) == 0 && connected != null)
                {
                    IPart connectedPart;
                    if (TryCastToPart(connected, out connectedPart) && Enqueue(connectedPart, stack, visited))
                    {
                        stats.SeedParts++;
                    }
                }
            }

            for (uint i = 0; i < subunitCount; i++)
            {
                ISubunit subunit;
                if (topology.GetSubunit(i, out subunit) != 0 || subunit == null) continue;

                IPart subunitPart;
                if (TryCastToPart(subunit, out subunitPart) && Enqueue(subunitPart, stack, visited))
                {
                    stats.SeedParts++;
                }
            }

            if (stats.SeedParts == 0)
            {
                // Некоторые драйверы не отдают топологию через коннекторы —
                // перебираем локальные id напрямую.
                stats.UsedFallback = true;

                bool foundAny = false;
                int consecutiveMisses = 0;

                for (uint localId = 0; localId < 65536; localId++)
                {
                    IPart part;
                    if (topology.GetPartById(localId, out part) != 0 || part == null)
                    {
                        if (foundAny)
                        {
                            consecutiveMisses++;
                            if (consecutiveMisses > 4096) break;
                        }
                        continue;
                    }

                    foundAny = true;
                    consecutiveMisses = 0;

                    if (Enqueue(part, stack, visited)) stats.SeedParts++;
                }
            }

            int changed = 0;
            while (stack.Count > 0)
            {
                IPart part = stack.Pop();
                if (part == null) continue;

                stats.PartsScanned++;
                if (action(part)) changed++;

                IPartsList incoming;
                if (part.EnumPartsIncoming(out incoming) == 0 && incoming != null)
                {
                    CollectFrom(incoming, stack, visited);
                }

                IPartsList outgoing;
                if (part.EnumPartsOutgoing(out outgoing) == 0 && outgoing != null)
                {
                    CollectFrom(outgoing, stack, visited);
                }
            }

            return changed;
        }

        private static void CollectFrom(IPartsList list, Stack<IPart> stack, HashSet<string> visited)
        {
            if (list == null) return;

            uint count;
            if (list.GetCount(out count) != 0) return;

            for (uint i = 0; i < count; i++)
            {
                IPart next;
                if (list.GetPart(i, out next) != 0 || next == null) continue;
                Enqueue(next, stack, visited);
            }
        }

        private static bool Enqueue(IPart part, Stack<IPart> stack, HashSet<string> visited)
        {
            if (part == null) return false;

            string key = GetPartId(part);
            if (string.IsNullOrWhiteSpace(key))
            {
                IntPtr unknown = IntPtr.Zero;
                try
                {
                    unknown = Marshal.GetIUnknownForObject(part);
                    key = "ptr:" + unknown.ToInt64().ToString("X");
                }
                catch
                {
                    key = Guid.NewGuid().ToString("N");
                }
                finally
                {
                    if (unknown != IntPtr.Zero) Marshal.Release(unknown);
                }
            }

            if (!visited.Add(key)) return false;

            stack.Push(part);
            return true;
        }

        private static bool TryCastToPart(object comObject, out IPart part)
        {
            part = comObject as IPart;
            if (part != null) return true;
            if (comObject == null) return false;

            IntPtr unknown = IntPtr.Zero;
            IntPtr partPtr = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(comObject);
                Guid partIid = typeof(IPart).GUID;
                if (Marshal.QueryInterface(unknown, ref partIid, out partPtr) != 0 || partPtr == IntPtr.Zero)
                {
                    return false;
                }

                part = (IPart)Marshal.GetObjectForIUnknown(partPtr);
                return part != null;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (partPtr != IntPtr.Zero) Marshal.Release(partPtr);
                if (unknown != IntPtr.Zero) Marshal.Release(unknown);
            }
        }

        private static string GetPartId(IPart part)
        {
            if (part == null) return null;

            string globalId;
            if (part.GetGlobalId(out globalId) == 0 && !string.IsNullOrWhiteSpace(globalId)) return globalId;

            uint localId;
            if (part.GetLocalId(out localId) == 0) return "local:" + localId;

            return null;
        }

        private static bool DisableAgcOnPart(IPart part)
        {
            IAudioAutoGainControl agc = ActivatePartInterface<IAudioAutoGainControl>(part);
            if (agc == null) return false;

            Guid ctx = Guid.Empty;
            if (agc.SetEnabled(false, ref ctx) != 0) return false;

            Log.Write("AUDIO", "agc control OFF: " + GetPartName(part));
            return true;
        }

        private static bool DisableBoostOnPart(IPart part)
        {
            if (part == null || !IsBoostPart(part)) return false;

            IAudioVolumeLevel volumeLevel = ActivatePartInterface<IAudioVolumeLevel>(part);
            if (volumeLevel == null) return false;

            uint channelCount;
            if (volumeLevel.GetChannelCount(out channelCount) != 0 || channelCount == 0) return false;

            bool changed = false;
            Guid ctx = Guid.Empty;

            for (uint channel = 0; channel < channelCount; channel++)
            {
                float minDb, maxDb, stepDb;
                if (volumeLevel.GetLevelRange(channel, out minDb, out maxDb, out stepDb) != 0) continue;
                if (volumeLevel.SetLevel(channel, minDb, ref ctx) == 0) changed = true;
            }

            if (changed) Log.Write("AUDIO", "boost control OFF: " + GetPartName(part));
            return changed;
        }

        private static bool IsBoostPart(IPart part)
        {
            Guid subType;
            if (part.GetSubType(out subType) == 0 && subType == KSAUDFNAME_MICROPHONE_BOOST) return true;

            string name = GetPartName(part);
            if (string.IsNullOrWhiteSpace(name)) return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("boost") || lower.Contains("gain") || lower.Contains("usil");
        }

        private static string GetPartName(IPart part)
        {
            if (part == null) return "<null>";

            string name;
            if (part.GetName(out name) == 0 && !string.IsNullOrWhiteSpace(name)) return name;

            return "<unknown>";
        }

        private static T ActivatePartInterface<T>(IPart part) where T : class
        {
            if (part == null) return null;

            Guid iid = typeof(T).GUID;
            object obj;
            if (part.Activate(CLSCTX_ALL, ref iid, out obj) != 0 || obj == null) return null;

            return obj as T;
        }

        private static string StateToString(int state)
        {
            if ((state & 1) != 0) return "active";
            if ((state & 2) != 0) return "disabled";
            if ((state & 4) != 0) return "notpresent";
            if ((state & 8) != 0) return "unplugged";
            return "unknown(" + state + ")";
        }

        private static string Hr(int hr)
        {
            return "0x" + hr.ToString("X8");
        }
    }
}

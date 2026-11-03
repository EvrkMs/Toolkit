using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Toolkit
{
    /// <summary>Audio: поиск и перечисление устройств, громкость, состояния (COM IMMDevice).</summary>
    internal static partial class AudioVerb
    {
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

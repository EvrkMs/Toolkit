using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Toolkit
{
    /// <summary>Audio: обход топологии устройства, AGC и усиление микрофона (COM IPart).</summary>
    internal static partial class AudioVerb
    {
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
    }
}

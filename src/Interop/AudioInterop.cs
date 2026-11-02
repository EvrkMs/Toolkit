using System;
using System.Runtime.InteropServices;

namespace Toolkit
{
    internal enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(2)]
        public ushort wReserved1;

        [FieldOffset(4)]
        public ushort wReserved2;

        [FieldOffset(6)]
        public ushort wReserved3;

        [FieldOffset(8)]
        public uint ulVal;

        [FieldOffset(8)]
        public IntPtr ptr;

        public static PROPVARIANT FromUInt32(uint value)
        {
            PROPVARIANT pv = new PROPVARIANT();
            pv.vt = 19; // VT_UI4
            pv.ulVal = value;
            return pv;
        }
    }

    internal static class Ole32
    {
        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PROPVARIANT pvar);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);

        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

        [PreserveSig]
        int GetState(out int pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);

        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);

        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);

        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);

        [PreserveSig]
        int Commit();
    }

    // Недокументированный, но стабильный с Windows 7 интерфейс; порядок методов критичен.
    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    internal class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, out IntPtr ppFormat);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);

        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);

        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PROPERTYKEY key, out PROPVARIANT pv);

        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PROPERTYKEY key, ref PROPVARIANT pv);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);

        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bVisible);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr pNotify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr pNotify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    }

    [ComImport]
    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceTopology
    {
        [PreserveSig]
        int GetConnectorCount(out uint pCount);

        [PreserveSig]
        int GetConnector(uint nIndex, out IConnector ppConnector);

        [PreserveSig]
        int GetSubunitCount(out uint pCount);

        [PreserveSig]
        int GetSubunit(uint nIndex, out ISubunit ppSubunit);

        [PreserveSig]
        int GetPartById(uint nId, out IPart ppPart);

        [PreserveSig]
        int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrDeviceId);

        [PreserveSig]
        int GetSignalPath(IPart pIPartFrom, IPart pIPartTo, [MarshalAs(UnmanagedType.Bool)] bool bRejectMixedPaths, out IPartsList ppParts);
    }

    [ComImport]
    [Guid("9c2c4058-23f5-41de-877a-df3af236a09e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IConnector
    {
        [PreserveSig]
        int GetType(out int pType);

        [PreserveSig]
        int GetDataFlow(out int pFlow);

        [PreserveSig]
        int ConnectTo(IConnector pConnectTo);

        [PreserveSig]
        int Disconnect();

        [PreserveSig]
        int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool pbConnected);

        [PreserveSig]
        int GetConnectedTo(out IConnector ppConTo);

        [PreserveSig]
        int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrConnectorId);

        [PreserveSig]
        int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrDeviceId);
    }

    [ComImport]
    [Guid("82149A85-DBA6-4487-86BB-EA8F7FEFCC71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISubunit
    {
    }

    [ComImport]
    [Guid("6DAA848C-5EB0-45CC-AEA5-998A2CDA1FFB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPartsList
    {
        [PreserveSig]
        int GetCount(out uint pCount);

        [PreserveSig]
        int GetPart(uint nIndex, out IPart ppPart);
    }

    [ComImport]
    [Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPart
    {
        [PreserveSig]
        int GetName([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrName);

        [PreserveSig]
        int GetLocalId(out uint pnId);

        [PreserveSig]
        int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrGlobalId);

        [PreserveSig]
        int GetPartType(out int pPartType);

        [PreserveSig]
        int GetSubType(out Guid pSubType);

        [PreserveSig]
        int GetControlInterfaceCount(out uint pCount);

        [PreserveSig]
        int GetControlInterface(uint nIndex, out IControlInterface ppInterfaceDesc);

        [PreserveSig]
        int EnumPartsIncoming(out IPartsList ppParts);

        [PreserveSig]
        int EnumPartsOutgoing(out IPartsList ppParts);

        [PreserveSig]
        int GetTopologyObject(out IDeviceTopology ppTopology);

        [PreserveSig]
        int Activate(int dwClsContext, ref Guid refiid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

        [PreserveSig]
        int RegisterControlChangeCallback(ref Guid riid, IControlChangeNotify pNotify);

        [PreserveSig]
        int UnregisterControlChangeCallback(IControlChangeNotify pNotify);
    }

    [ComImport]
    [Guid("45d37c3f-5140-444a-ae24-400789f3cbf3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IControlInterface
    {
    }

    [ComImport]
    [Guid("A09513ED-C709-4D21-BD7B-5F34C47F3947")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IControlChangeNotify
    {
    }

    [ComImport]
    [Guid("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioVolumeLevel
    {
        [PreserveSig]
        int GetChannelCount(out uint pcChannels);

        [PreserveSig]
        int GetLevelRange(uint nChannel, out float pfMinLevelDB, out float pfMaxLevelDB, out float pfStepping);

        [PreserveSig]
        int GetLevel(uint nChannel, out float pfLevelDB);

        [PreserveSig]
        int SetLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);

        [PreserveSig]
        int SetLevelUniform(float fLevelDB, ref Guid pguidEventContext);

        [PreserveSig]
        int SetLevelAllChannels([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] float[] aLevelsDB, uint cChannels, ref Guid pguidEventContext);
    }

    [ComImport]
    [Guid("85401FD4-6DE4-4B9D-9869-2D6753A82F3C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioAutoGainControl
    {
        [PreserveSig]
        int GetEnabled([MarshalAs(UnmanagedType.Bool)] out bool pbEnabled);

        [PreserveSig]
        int SetEnabled([MarshalAs(UnmanagedType.Bool)] bool bEnable, ref Guid pguidEventContext);
    }
}

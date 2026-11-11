using System.Runtime.InteropServices;

namespace CoreWindows.Inventory;

/// <summary>Resolução e taxa de atualização atuais, via user32.dll (o WMI não informa isso por monitor).</summary>
internal static class NativeDisplays
{
    private const uint AttachedToDesktop = 0x1;
    private const uint PrimaryDevice = 0x4;
    private const uint MirroringDriver = 0x8;
    private const uint MonitorActive = 0x1;
    private const uint GetDeviceInterfaceName = 0x1;
    private const int CurrentSettings = -1;

    public static IReadOnlyList<DisplayOutput> Read()
    {
        var outputs = new List<DisplayOutput>();

        foreach (var adapter in Devices(null, 0))
        {
            bool inUse = (adapter.StateFlags & AttachedToDesktop) != 0
                      && (adapter.StateFlags & MirroringDriver) == 0;
            if (!inUse || ReadMode(adapter.DeviceName) is not { } mode)
                continue;

            bool primary = (adapter.StateFlags & PrimaryDevice) != 0;

            // Em modo "duplicar", uma mesma saída tem mais de um monitor: por isso o laço.
            foreach (var monitor in Devices(adapter.DeviceName, GetDeviceInterfaceName))
            {
                if ((monitor.StateFlags & MonitorActive) != 0)
                    outputs.Add(new DisplayOutput(monitor.DeviceID, mode, primary));
            }
        }

        return outputs.Count > 0
            ? outputs
            : throw new InvalidOperationException("Nenhuma saída de vídeo ativa encontrada.");
    }

    private static IEnumerable<DISPLAY_DEVICE> Devices(string? parent, uint flags)
    {
        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(parent, i, ref device, flags))
                yield break;

            yield return device;
        }
    }

    private static DisplayMode? ReadMode(string adapterName)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(adapterName, CurrentSettings, ref mode)
            ? new DisplayMode((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency)
            : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    // Os campos e sua ordem precisam ser idênticos aos structs do Windows (wingdi.h).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
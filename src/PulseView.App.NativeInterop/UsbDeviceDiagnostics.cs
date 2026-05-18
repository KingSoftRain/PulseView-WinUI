using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PulseView.App.NativeInterop;

public sealed record UsbPipeDiagnostic(byte PipeId, string PipeType, int MaximumPacketSize, byte Interval)
{
    public string Summary => $"{PipeType} 0x{PipeId:X2}, packet {MaximumPacketSize}, interval {Interval}";
}

public sealed record UsbInterfaceDiagnostic(byte InterfaceNumber, byte AlternateSetting, byte EndpointCount, string Description)
{
    public string Summary => $"interface {InterfaceNumber}, alt {AlternateSetting}, endpoints {EndpointCount}";
}

public sealed record UsbDeviceDiagnostic(
    string DevicePath,
    Guid InterfaceGuid,
    bool CanOpen,
    UsbInterfaceDiagnostic? Interface,
    IReadOnlyList<UsbPipeDiagnostic> Pipes,
    string? ErrorMessage)
{
    public string Summary => CanOpen && Interface is not null
        ? $"WinUSB open: {Interface.Summary}; {Pipes.Count} pipes"
        : $"WinUSB unavailable: {ErrorMessage}";
}

public static class WinUsbDeviceInspector
{
    private const int DigcfPresent = 0x00000002;
    private const int DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IReadOnlyList<UsbDeviceDiagnostic> Inspect(Guid interfaceGuid, string requiredDevicePathFragment)
    {
        if (!OperatingSystem.IsWindows()) {
            return [];
        }

        var paths = EnumerateDevicePaths(interfaceGuid)
            .Where(path => path.Contains(requiredDevicePathFragment, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return paths.Select(path => InspectDevicePath(interfaceGuid, path)).ToArray();
    }

    public static IReadOnlyList<string> EnumerateDevicePaths(Guid interfaceGuid)
    {
        if (!OperatingSystem.IsWindows()) {
            return [];
        }

        var deviceInfoSet = SetupDiGetClassDevs(
            ref interfaceGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidHandleValue) {
            return [];
        }

        try {
            var paths = new List<string>();
            var interfaceData = new SpDeviceInterfaceData {
                CbSize = Marshal.SizeOf<SpDeviceInterfaceData>(),
            };

            for (var index = 0U; SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData); index++) {
                var requiredSize = 0;
                _ = SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                if (requiredSize <= 0) {
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal(requiredSize);
                try {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, ref requiredSize, IntPtr.Zero)) {
                        continue;
                    }

                    var path = ReadDevicePath(detailBuffer);
                    if (!string.IsNullOrWhiteSpace(path)) {
                        paths.Add(path);
                    }
                }
                finally {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }

            return paths;
        }
        finally {
            _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static UsbDeviceDiagnostic InspectDevicePath(Guid interfaceGuid, string devicePath)
    {
        using var deviceHandle = CreateFile(
            devicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (deviceHandle.IsInvalid) {
            return CreateFailedDiagnostic(devicePath, interfaceGuid, Marshal.GetLastPInvokeError());
        }

        if (!WinUsbInitialize(deviceHandle, out var winUsbHandle)) {
            return CreateFailedDiagnostic(devicePath, interfaceGuid, Marshal.GetLastPInvokeError());
        }

        try {
            if (!WinUsbQueryInterfaceSettings(winUsbHandle, 0, out var interfaceDescriptor)) {
                return CreateFailedDiagnostic(devicePath, interfaceGuid, Marshal.GetLastPInvokeError());
            }

            var pipes = new List<UsbPipeDiagnostic>();
            for (byte index = 0; index < interfaceDescriptor.NumEndpoints; index++) {
                if (WinUsbQueryPipe(winUsbHandle, 0, index, out var pipeInformation)) {
                    pipes.Add(new UsbPipeDiagnostic(
                        pipeInformation.PipeId,
                        PipeTypeName(pipeInformation.PipeType),
                        pipeInformation.MaximumPacketSize,
                        pipeInformation.Interval));
                }
            }

            return new UsbDeviceDiagnostic(
                devicePath,
                interfaceGuid,
                CanOpen: true,
                new UsbInterfaceDiagnostic(
                    interfaceDescriptor.InterfaceNumber,
                    interfaceDescriptor.AlternateSetting,
                    interfaceDescriptor.NumEndpoints,
                    $"class 0x{interfaceDescriptor.InterfaceClass:X2}, subclass 0x{interfaceDescriptor.InterfaceSubClass:X2}, protocol 0x{interfaceDescriptor.InterfaceProtocol:X2}"),
                pipes,
                ErrorMessage: null);
        }
        finally {
            _ = WinUsbFree(winUsbHandle);
        }
    }

    private static UsbDeviceDiagnostic CreateFailedDiagnostic(string devicePath, Guid interfaceGuid, int error)
    {
        return new UsbDeviceDiagnostic(
            devicePath,
            interfaceGuid,
            CanOpen: false,
            Interface: null,
            Pipes: [],
            ErrorMessage: new Win32Exception(error).Message);
    }

    private static string? ReadDevicePath(IntPtr detailBuffer)
    {
        var path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
        if (!string.IsNullOrWhiteSpace(path) && path.StartsWith(@"\\?\", StringComparison.Ordinal)) {
            return path;
        }

        path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 8));
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static string PipeTypeName(UsbPipeType pipeType)
    {
        return pipeType switch
        {
            UsbPipeType.Control => "control",
            UsbPipeType.Isochronous => "isochronous",
            UsbPipeType.Bulk => "bulk",
            UsbPipeType.Interrupt => "interrupt",
            _ => $"type {(int)pipeType}",
        };
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        ref int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("winusb.dll", EntryPoint = "WinUsb_Initialize", SetLastError = true)]
    private static extern bool WinUsbInitialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", EntryPoint = "WinUsb_Free", SetLastError = true)]
    private static extern bool WinUsbFree(IntPtr interfaceHandle);

    [DllImport("winusb.dll", EntryPoint = "WinUsb_QueryInterfaceSettings", SetLastError = true)]
    private static extern bool WinUsbQueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

    [DllImport("winusb.dll", EntryPoint = "WinUsb_QueryPipe", SetLastError = true)]
    private static extern bool WinUsbQueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WinUsbPipeInformation pipeInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte NumEndpoints;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte Interface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinUsbPipeInformation
    {
        public UsbPipeType PipeType;
        public byte PipeId;
        public short MaximumPacketSize;
        public byte Interval;
    }

    private enum UsbPipeType
    {
        Control = 0,
        Isochronous = 1,
        Bulk = 2,
        Interrupt = 3,
    }
}

using PulseView.App.NativeInterop;

namespace PulseView.App.ViewModels;

public sealed record CaptureDeviceConnection(bool IsConnected, string Summary, IReadOnlyList<string> Details)
{
    public static CaptureDeviceConnection NotApplicable(string summary)
    {
        return new CaptureDeviceConnection(false, summary, []);
    }
}

public interface IHardwareTransportInspector
{
    CaptureDeviceConnection Inspect(CaptureDeviceOption device);
}

public sealed class WinUsbHardwareTransportInspector : IHardwareTransportInspector
{
    private const string SLogicPathFragment = "vid_359f&pid_0300";
    private static readonly Guid DefaultSLogicInterfaceGuid = Guid.Parse("CDB3B5AD-293B-4663-AA36-1AAE46463776");

    public CaptureDeviceConnection Inspect(CaptureDeviceOption device)
    {
        if (device.Kind != CaptureDeviceKind.SLogicCombo8Logic) {
            return CaptureDeviceConnection.NotApplicable(device.Status);
        }

        if (!OperatingSystem.IsWindows()) {
            return CaptureDeviceConnection.NotApplicable("WinUSB transport is only available on Windows.");
        }

        var interfaceGuid = Guid.TryParse(device.DeviceInterfaceGuid, out var parsedGuid)
            ? parsedGuid
            : DefaultSLogicInterfaceGuid;
        var diagnostics = WinUsbDeviceInspector.Inspect(interfaceGuid, SLogicPathFragment);
        if (diagnostics.Count == 0 && interfaceGuid != DefaultSLogicInterfaceGuid) {
            diagnostics = WinUsbDeviceInspector.Inspect(DefaultSLogicInterfaceGuid, SLogicPathFragment);
        }

        if (diagnostics.Count == 0) {
            return new CaptureDeviceConnection(
                false,
                "No WinUSB device interface path was found for SLogic Combo 8.",
                [$"Interface GUID: {interfaceGuid}"]);
        }

        var best = diagnostics.FirstOrDefault(diagnostic => diagnostic.CanOpen) ?? diagnostics[0];
        var details = new List<string> {
            $"Path: {best.DevicePath}",
            $"Interface GUID: {best.InterfaceGuid}",
        };
        if (best.Interface is not null) {
            details.Add(best.Interface.Summary);
            details.Add(best.Interface.Description);
        }

        details.AddRange(best.Pipes.Select(pipe => pipe.Summary));
        if (!string.IsNullOrWhiteSpace(best.ErrorMessage)) {
            details.Add(best.ErrorMessage);
        }

        return new CaptureDeviceConnection(best.CanOpen, best.Summary, details);
    }
}

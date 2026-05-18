namespace PulseView.App.ViewModels;

public interface IHardwareDeviceProbe
{
    IReadOnlyList<CaptureDeviceOption> ScanDevices();
}

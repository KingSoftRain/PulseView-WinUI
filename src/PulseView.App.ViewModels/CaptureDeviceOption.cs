namespace PulseView.App.ViewModels;

public enum CaptureDeviceKind
{
    Demo,
    SLogicCombo8Logic,
    SLogicCombo8OtherMode,
    UnknownUsb,
}

public sealed record CaptureDeviceOption(
    string Id,
    string DisplayName,
    CaptureDeviceKind Kind,
    string DriverName,
    string Status,
    bool CanAcquire,
    string? DeviceInterfaceGuid = null)
{
    public bool IsDemo => Kind == CaptureDeviceKind.Demo;

    public bool IsSLogicCombo8 => Kind is CaptureDeviceKind.SLogicCombo8Logic or CaptureDeviceKind.SLogicCombo8OtherMode;

    public override string ToString() => DisplayName;
}

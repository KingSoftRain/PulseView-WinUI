using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PulseView.App.ViewModels;

public sealed class WindowsHardwareDeviceProbe : IHardwareDeviceProbe
{
    public IReadOnlyList<CaptureDeviceOption> ScanDevices()
    {
        if (!OperatingSystem.IsWindows()) {
            return [];
        }

        var devices = new List<CaptureDeviceOption>();
        try {
            using var usbRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbRoot is null) {
                return devices;
            }

            foreach (var deviceKeyName in usbRoot.GetSubKeyNames()) {
                using var deviceKey = usbRoot.OpenSubKey(deviceKeyName);
                if (deviceKey is null) {
                    continue;
                }

                foreach (var instanceKeyName in deviceKey.GetSubKeyNames()) {
                    using var instanceKey = deviceKey.OpenSubKey(instanceKeyName);
                    if (instanceKey is null) {
                        continue;
                    }

                    var instanceId = $@"USB\{deviceKeyName}\{instanceKeyName}";
                    var displayName = ReadDisplayName(instanceKey, deviceKeyName);
                    var deviceInterfaceGuid = ReadDeviceInterfaceGuid(instanceKey);
                    var text = $"{displayName} {instanceId} {ReadMultiString(instanceKey, "HardwareID")} {ReadMultiString(instanceKey, "CompatibleIDs")}";
                    var option = ClassifyDevice(instanceId, displayName, text, deviceInterfaceGuid);
                    if (option is not null && devices.All(device => !StringComparer.OrdinalIgnoreCase.Equals(device.Id, option.Id))) {
                        devices.Add(option);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException) {
            devices.Add(new CaptureDeviceOption(
                "scan-denied",
                "USB scan permission denied",
                CaptureDeviceKind.UnknownUsb,
                "Windows registry",
                "Run the app with enough rights to scan USB devices.",
                CanAcquire: false));
        }
        catch (IOException ex) {
            devices.Add(new CaptureDeviceOption(
                "scan-failed",
                "USB scan failed",
                CaptureDeviceKind.UnknownUsb,
                "Windows registry",
                ex.Message,
                CanAcquire: false));
        }

        return DeduplicateDevices(devices);
    }

    private static CaptureDeviceOption? ClassifyDevice(string instanceId, string displayName, string text, string? deviceInterfaceGuid)
    {
        if (ContainsAny(text, "RV CMSIS-DAP", "DAPLINK", "CMSIS-DAP", "CKLINK", "VID_D6E7&PID_3507")) {
            return null;
        }

        var hasSLogicVid = ContainsAny(text, "VID_359F");
        var hasLogicModePid = ContainsAny(text, "VID_359F&PID_0300");
        var hasLogicModeName = ContainsAny(text, "USB TO LA", "SIPEED USB TO LA", "SLOGIC COMBO", "SLOGIC16");
        if ((hasLogicModePid || hasLogicModeName) && (!hasSLogicVid || hasLogicModePid)) {
            return new CaptureDeviceOption(
                instanceId,
                "SLogic Combo 8",
                CaptureDeviceKind.SLogicCombo8Logic,
                "SLogic Combo 8",
                "Logic-analyzer mode detected. WinUSB transport diagnostics are available.",
                CanAcquire: true,
                deviceInterfaceGuid);
        }

        return null;
    }

    private static IReadOnlyList<CaptureDeviceOption> DeduplicateDevices(IReadOnlyList<CaptureDeviceOption> devices)
    {
        return devices
            .GroupBy(GetDeviceIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(device => device.DeviceInterfaceGuid is not null)
                .ThenByDescending(device => device.Id.Contains("VID_359F&PID_0300", StringComparison.OrdinalIgnoreCase))
                .First())
            .ToArray();
    }

    private static string GetDeviceIdentityKey(CaptureDeviceOption device)
    {
        return device.Kind == CaptureDeviceKind.SLogicCombo8Logic
            ? "USB\\VID_359F&PID_0300"
            : device.Id;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadDeviceInterfaceGuid(RegistryKey instanceKey)
    {
        using var parametersKey = instanceKey.OpenSubKey("Device Parameters");
        return parametersKey?.GetValue("DeviceInterfaceGUID") as string;
    }

    [SupportedOSPlatform("windows")]
    private static string ReadDisplayName(RegistryKey key, string fallback)
    {
        var friendlyName = key.GetValue("FriendlyName") as string;
        if (!string.IsNullOrWhiteSpace(friendlyName)) {
            return friendlyName;
        }

        var deviceDescription = key.GetValue("DeviceDesc") as string;
        if (!string.IsNullOrWhiteSpace(deviceDescription)) {
            var separator = deviceDescription.LastIndexOf(';');
            return separator >= 0 && separator + 1 < deviceDescription.Length
                ? deviceDescription[(separator + 1)..]
                : deviceDescription;
        }

        return fallback;
    }

    [SupportedOSPlatform("windows")]
    private static string ReadMultiString(RegistryKey key, string valueName)
    {
        return key.GetValue(valueName) switch
        {
            string value => value,
            string[] values => string.Join(' ', values),
            _ => string.Empty,
        };
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}

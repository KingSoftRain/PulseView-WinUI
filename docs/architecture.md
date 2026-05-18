# Architecture

PulseView.WinUI is split into explicit layers:

```text
PulseView.Core.Native
PulseView.NativeBridge
PulseView.App.NativeInterop
PulseView.App.ViewModels
PulseView.App.Controls
PulseView.App
PulseView.Rendering.Native
```

`PulseView.Core.Native` is C++23 and UI-free. `PulseView.NativeBridge` exposes a
stable C ABI. `PulseView.App.NativeInterop` owns P/Invoke and `SafeHandle`
wrappers. ViewModels call service abstractions, not native pointers. WinUI owns
the visual shell and file picker workflow.

The current app can load a built-in demo device, start and stop demo acquisition,
query multi-channel digital and analog viewport primitives, create a native
session, open a capture file path through the ABI, query finite byte-derived
viewport digital spans and duration from the native session, and surface native
errors as managed exceptions/status text.

`PulseView.Rendering.Native` consumes viewport primitives from
`PulseView.Core.Native` and owns the Direct2D/DXGI drawing path. The renderer is
allowed to depend on core-native data/query APIs, but the core layer remains
independent of WinUI, Direct2D, DXGI, and C#.

# Rendering Model

Waveform rendering is reserved for `PulseView.Rendering.Native`.

The current milestone adds the first native rendering prototype:

- `WaveformPanel` is a WinUI control that owns a `SwapChainPanel`.
- `NativeWaveformRenderer` wraps the native renderer handle with `SafeHandle`.
- `PulseView.Rendering.Native` creates Direct3D, DXGI swap chain, and Direct2D
  target resources. It requests a hardware Direct3D device first, uses
  Direct2D over that device, and records the active adapter in the rendering
  log.
- The native renderer attaches the swap chain to WinUI through
  `ISwapChainPanelNative`.
- WinUI 3 requires the `microsoft.ui.xaml.media.dxinterop.h` interface from the
  Windows App SDK WinUI package. The older
  `windows.ui.xaml.media.dxinterop.h` interface targets UWP XAML and returns
  `E_NOINTERFACE` for `Microsoft.UI.Xaml.Controls.SwapChainPanel`.
- `ShellViewModel` owns viewport state: start time and seconds per pixel.
- `ShellViewModel` also owns the current demo-device acquisition state for the
  prototype path: load device, start acquisition, stop acquisition, and elapsed
  capture duration.
- `WaveformPanel` forwards viewport state to native code through dependency
  properties.
- The WinUI page maps mouse-wheel input to pointer-centered zoom and left-button
  dragging to horizontal waveform pan. Zoom uses a precomputed 1-2-5 scale
  ladder and binary search to choose the next scale step.
- When the pointer is in the blank area to the right of a short capture, zoom-in
  still uses the next 1-2-5 scale step but pans only enough for the capture end
  to approach the pointer; once the capture reaches that pointer position,
  normal pointer-centered zoom resumes.
- `PulseView.Core.Native` produces digital waveform viewport primitives
  (`DigitalSpan`) for the requested visible range.
- Opened captures are currently converted into a finite byte-derived digital
  sample stream at a fixed sample rate and expose duration through the session
  ABI.
- The demo device currently generates eight digital channels and two analog
  channels in managed viewport primitives. Dense digital transitions are
  downsampled to explicit dense spans when zoomed far out, and those spans draw
  as filled blocks instead of misleading low/high lines.
- After a capture is opened, `SessionService` queries the current session for
  viewport spans and sends only those spans to `WaveformPanel`.
- The renderer draws a synthetic grid plus multiple digital and analog tracks
  from viewport primitives, or the built-in synthetic fallback before a source
  is loaded; the output responds to pan and zoom.
- The native renderer owns channel labels as DirectWrite text and reserves a
  fixed left plot margin, so labels do not become XAML elements and the plotted
  waveform area stays aligned with wheel zoom coordinates.
- Analog overview rendering follows PulseView's trace/envelope split: normal
  zoom uses per-pixel line segments, while high samples-per-pixel views use
  min/max envelope rectangles.
- The app batches viewport, channel counts, digital spans, and analog segments
  into one native render submission to avoid multiple redraws per viewport
  change.
- Managed regression tests cover overview primitive budgets so dense zoom levels
  stay bounded by viewport width instead of capture sample count.
- The renderer reuses Direct2D brushes across frames and sets maximum DXGI frame
  latency to one to reduce interaction delay on integrated GPUs.
- Reset fits the current capture/demo duration into the waveform viewport width
  when a source has data.
- A zero-span session viewport is rendered as empty data, while the synthetic
  fallback is used only before a capture is loaded.
- The managed UI does not create XAML elements for samples, edges, or protocol
  annotations.
- `PulseView.Rendering.Native` already builds as a separate native DLL and is
  copied beside the WinUI app output.

The next rendering step is to move demo/real capture generation behind a common
device abstraction while preserving the same viewport primitive ABI.

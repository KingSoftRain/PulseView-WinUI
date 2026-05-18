# SLogic Combo 8 Support

The current app has an application-level SLogic Combo 8 profile. It is not yet
the final USB streaming transport.

Implemented in this milestone:

- The device selector is now a capture-source list. It includes the virtual
  `Demo Device` and real logic-analyzer devices only.
- SLogic Combo 8 logic mode is displayed as `SLogic Combo 8`; debugger, serial, and
  other alternate modes are intentionally filtered out of the selector.
- Device scan looks for Windows USB registry entries that match the logic
  analyzer identity (`USB TO LA`, `SLogic`, or known Sipeed VID text).
- Device scan also detects likely Combo 8 debugger/serial mode entries such as
  `RV CMSIS-DAP`, `DAPLink`, and `USB Serial Device`.
- The UI exposes device scan/connect, active digital channel count, sample
  rate, sample count, trigger channel/edge, and decoder controls.
- SLogic connect now opens the WinUSB interface in diagnostics mode and reports
  the device path, interface descriptor, and endpoint pipes. Acquisition command
  transfer is still intentionally gated behind a later protocol step.
- Loading `SLogic Combo 8` creates an 8-channel digital session and renders
  low-level idle baselines while the real streaming transport is still pending.
  When the time scale separates samples clearly, the same view also renders
  sample point markers at the selected sample rate.
- The Windows sample-rate list is limited by active channel count:
  - 1-2 channels: up to 80 MHz.
  - 3-4 channels: up to 40 MHz.
  - 5-8 channels: up to 20 MHz for now, leaving margin below the documented
    Windows transport limit.
- Demo capture now includes a D0 UART 115200 8N1 stream so the decoder panel can
  be tested against visible waveform data.
- UART decode annotations are produced from the demo source after a decoder is
  added from the Decoder page. Each decoder row can keep its own settings and
  line color.

Hardware notes:

- Sipeed documents that logic-analyzer mode is selected by switching the
  indicator to blue and that Windows should show a `USB TO LA` device.
- On the development machine, logic-analyzer mode has been verified as
  `USB\VID_359F&PID_0300\SI_8CH` with friendly name `USB TO LA`.
- The same device is bound to Microsoft's `WINUSB` service and exposes
  interface GUID `{CDB3B5AD-293B-4663-AA36-1AAE46463776}`.
- The same hardware can also enumerate as DAPLink, CKLink, or UART. Those modes
  are useful, but they are not the logic-analyzer acquisition mode.
- The streaming driver should not issue USB control or bulk transfers until the
  scanner sees the logic-analyzer identity. This avoids sending acquisition
  commands to a debugger or serial interface.

References:

- https://en.wiki.sipeed.com/hardware/en/logic_analyzer/combo8/use_logic_function.html
- https://wiki.sipeed.com/hardware/en/logic_analyzer/combo8/

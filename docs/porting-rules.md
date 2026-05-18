# Porting Rules

- Keep native core code independent of WinUI and XAML.
- Keep libsigrok/libsigrokdecode integration inside native core boundaries.
- Do not expose C++ types, STL containers, exceptions, or templates through the
  ABI.
- Do not store native pointers in ViewModels.
- Do not marshal complete sample buffers into C# for rendering.
- Prefer viewport primitive queries for waveform rendering.
- Add focused tests when changing ABI or ViewModel behavior.

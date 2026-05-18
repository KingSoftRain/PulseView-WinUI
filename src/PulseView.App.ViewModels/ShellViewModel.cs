using PulseView.App.NativeInterop;
using System.Diagnostics;

namespace PulseView.App.ViewModels;

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private const double DefaultSecondsPerPixel = 10.0e-6;
    private const double MinimumSecondsPerPixel = 1.0e-9;
    private const double MaximumSecondsPerPixel = 10.0;
    private const int MaximumCachedDecodeAnnotations = 8192;
    private const int MaximumVisibleDecodeAnnotations = 1024;
    private const double CaptureCompletionToleranceSeconds = 1.0e-12;
    private const double HardwareDigitalPointMinimumPixelSpacing = 7.0;
    private const byte DigitalPointFlag = 8;
    private static readonly CaptureDeviceOption DemoDevice = new(
        "demo",
        "Demo Device",
        CaptureDeviceKind.Demo,
        "Managed demo source",
        "Ready",
        CanAcquire: true);

    private static readonly CaptureSampleRateOption[] AllSampleRates = [
        new("1 MHz", 1_000_000),
        new("5 MHz", 5_000_000),
        new("10 MHz", 10_000_000),
        new("20 MHz", 20_000_000),
        new("40 MHz", 40_000_000),
        new("80 MHz", 80_000_000),
    ];

    private static readonly CaptureSampleCountOption[] AllSampleCounts = [
        new("100 k", 100_000),
        new("1 M", 1_000_000),
        new("10 M", 10_000_000),
    ];

    private static readonly ProtocolDecoderOption[] AllDecoders = [
        new(ProtocolDecoderKind.None, "No decoder"),
        new(ProtocolDecoderKind.Uart, "UART"),
    ];

    private static readonly DecoderColorOption[] AllDecoderColors = [
        new("Yellow", 0xFA, 0xCC, 0x15),
        new("Blue", 0x3B, 0x82, 0xF6),
        new("Cyan", 0x06, 0xB6, 0xD4),
        new("Teal", 0x14, 0xB8, 0xA6),
        new("Green", 0x22, 0xC5, 0x5E),
        new("Lime", 0x84, 0xCC, 0x16),
        new("Orange", 0xF9, 0x73, 0x16),
        new("Red", 0xEF, 0x44, 0x44),
        new("Rose", 0xF4, 0x3F, 0x5E),
        new("Pink", 0xEC, 0x48, 0x99),
        new("Purple", 0xA8, 0x55, 0xF7),
        new("Violet", 0x8B, 0x5C, 0xF6),
        new("Indigo", 0x63, 0x66, 0xF1),
        new("Sky", 0x0E, 0xA5, 0xE9),
        new("Amber", 0xF5, 0x9E, 0x0B),
        new("Slate", 0x94, 0xA3, 0xB8),
    ];

    private static readonly TriggerEdgeOption[] AllTriggerEdges = [
        new(TriggerEdgeKind.None, "No trigger"),
        new(TriggerEdgeKind.Rising, "Rising edge"),
        new(TriggerEdgeKind.Falling, "Falling edge"),
        new(TriggerEdgeKind.Either, "Either edge"),
    ];

    private static readonly int[] ChannelCountChoices = [1, 2, 4, 8];
    private static readonly int[] BaudRateChoices = [9_600, 57_600, 115_200, 1_000_000];
    private static readonly int[] DataBitChoices = [5, 6, 7, 8];
    private static readonly string[] StopBitChoices = ["1", "1.5", "2"];
    private static readonly string[] ParityChoices = ["None", "Odd", "Even", "Mark", "Space"];

    private readonly ISessionService _sessionService;
    private readonly IHardwareDeviceProbe _hardwareDeviceProbe;
    private readonly IHardwareTransportInspector _hardwareTransportInspector;
    private readonly DemoCaptureSource _demoCapture = new();
    private readonly Stopwatch _hardwareCaptureStopwatch = new();
    private string _statusMessage = "Ready";
    private string _currentFilePath = string.Empty;
    private CaptureDeviceOption[] _deviceOptions = [DemoDevice];
    private CaptureDeviceOption _selectedDevice = DemoDevice;
    private CaptureSampleRateOption[] _availableSampleRates = AllSampleRates[..5];
    private CaptureSampleRateOption _selectedSampleRate = AllSampleRates[2];
    private CaptureSampleCountOption _selectedSampleCount = AllSampleCounts[1];
    private ProtocolDecoderOption _selectedDecoder = AllDecoders[1];
    private TriggerEdgeOption _selectedTriggerEdge = AllTriggerEdges[0];
    private int _activeDigitalChannelCount = 8;
    private int _triggerChannelIndex;
    private int _decoderChannelIndex;
    private int _decoderBaudRate = 115_200;
    private int _decoderDataBits = 8;
    private string _decoderStopBits = StopBitChoices[0];
    private string _decoderParity = ParityChoices[0];
    private DecoderColorOption _decoderColor = AllDecoderColors[0];
    private string[] _decoderChannelOptions = [];
    private ConfiguredDecoder[] _configuredDecoders = [];
    private DecodedAnnotation[] _decodeAnnotations = [];
    private CaptureDeviceConnection? _deviceConnection;
    private int _nextDecoderId = 1;
    private int? _editingDecoderId;
    private int _signalCount;
    private int _digitalChannelCount = 1;
    private int _analogChannelCount;
    private double _durationSeconds;
    private bool _isBusy;
    private bool _isDemoDeviceLoaded;
    private bool _isHardwareDeviceLoaded;
    private bool _isAcquiring;
    private double _hardwareCapturedDurationSeconds;
    private double _viewportStartSeconds;
    private double _secondsPerPixel = DefaultSecondsPerPixel;

    public ShellViewModel()
        : this(new SessionService(), new WindowsHardwareDeviceProbe())
    {
    }

    public ShellViewModel(ISessionService sessionService)
        : this(sessionService, new WindowsHardwareDeviceProbe(), new WinUsbHardwareTransportInspector())
    {
    }

    public ShellViewModel(ISessionService sessionService, IHardwareDeviceProbe hardwareDeviceProbe)
        : this(sessionService, hardwareDeviceProbe, new WinUsbHardwareTransportInspector())
    {
    }

    public ShellViewModel(
        ISessionService sessionService,
        IHardwareDeviceProbe hardwareDeviceProbe,
        IHardwareTransportInspector hardwareTransportInspector)
    {
        _sessionService = sessionService;
        _hardwareDeviceProbe = hardwareDeviceProbe;
        _hardwareTransportInspector = hardwareTransportInspector;
        UpdateAvailableSampleRates();
        UpdateDecoderChannelOptions();
    }

    public string NativeBridgeVersion { get; } = NativeLibraryInfo.GetVersion();

    public string NativeRenderingVersion { get; } = NativeRenderingInfo.GetVersion();

    public IReadOnlyList<CaptureDeviceOption> DeviceOptions => _deviceOptions;

    public CaptureDeviceOption SelectedDevice
    {
        get => _selectedDevice;
        private set
        {
            if (SetProperty(ref _selectedDevice, value)) {
                OnPropertyChanged(nameof(DeviceStatusSummary));
                OnPropertyChanged(nameof(CanAcquireSelectedDevice));
                OnPropertyChanged(nameof(CanLoadSelectedDevice));
                OnPropertyChanged(nameof(IsSelectedDeviceLoaded));
            }
        }
    }

    public IReadOnlyList<int> DigitalChannelCountOptions => ChannelCountChoices;

    public IReadOnlyList<CaptureSampleRateOption> AvailableSampleRates => _availableSampleRates;

    public IReadOnlyList<CaptureSampleCountOption> SampleCountOptions => AllSampleCounts;

    public IReadOnlyList<ProtocolDecoderOption> DecoderOptions => AllDecoders;

    public IReadOnlyList<DecoderColorOption> DecoderColorOptions => AllDecoderColors;

    public IReadOnlyList<TriggerEdgeOption> TriggerEdgeOptions => AllTriggerEdges;

    public IReadOnlyList<string> DecoderChannelOptions => _decoderChannelOptions;

    public IReadOnlyList<int> DecoderBaudRateOptions => BaudRateChoices;

    public IReadOnlyList<int> DecoderDataBitOptions => DataBitChoices;

    public IReadOnlyList<string> DecoderStopBitOptions => StopBitChoices;

    public IReadOnlyList<string> DecoderParityOptions => ParityChoices;

    public CaptureSampleRateOption SelectedSampleRate => _selectedSampleRate;

    public CaptureSampleCountOption SelectedSampleCount => _selectedSampleCount;

    public ProtocolDecoderOption SelectedDecoder => _selectedDecoder;

    public TriggerEdgeOption SelectedTriggerEdge => _selectedTriggerEdge;

    public int ActiveDigitalChannelCount => _activeDigitalChannelCount;

    public int TriggerChannelIndex => _triggerChannelIndex;

    public int DecoderChannelIndex => _decoderChannelIndex;

    public int DecoderBaudRate => _decoderBaudRate;

    public int DecoderDataBits => _decoderDataBits;

    public string DecoderStopBits => _decoderStopBits;

    public string DecoderParity => _decoderParity;

    public DecoderColorOption DecoderColor => _decoderColor;

    public IReadOnlyList<ConfiguredDecoder> ConfiguredDecoders => _configuredDecoders;

    public IReadOnlyList<DecodedAnnotation> DecodeAnnotations => _decodeAnnotations;

    public IReadOnlyList<string> DeviceConnectionDetails => _deviceConnection?.Details ?? [];

    public bool CanAcquireSelectedDevice => SelectedDevice.IsDemo
        || (_isHardwareDeviceLoaded && SelectedDevice.Kind == CaptureDeviceKind.SLogicCombo8Logic);

    public bool CanLoadSelectedDevice => IsSelectedDeviceLoaded || SelectedDevice.CanAcquire;

    public bool IsSelectedDeviceLoaded => SelectedDevice.IsDemo
        ? IsDemoDeviceLoaded
        : _isHardwareDeviceLoaded && SelectedDevice.Kind == CaptureDeviceKind.SLogicCombo8Logic;

    public int DecoderRowCount => _configuredDecoders.Length;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentFilePath
    {
        get => _currentFilePath;
        private set => SetProperty(ref _currentFilePath, value);
    }

    public int SignalCount
    {
        get => _signalCount;
        private set
        {
            if (SetProperty(ref _signalCount, value)) {
                OnPropertyChanged(nameof(HasSession));
                OnPropertyChanged(nameof(SignalSummary));
            }
        }
    }

    public int DigitalChannelCount
    {
        get => _digitalChannelCount;
        private set
        {
            if (SetProperty(ref _digitalChannelCount, value)) {
                OnPropertyChanged(nameof(SignalSummary));
            }
        }
    }

    public int AnalogChannelCount
    {
        get => _analogChannelCount;
        private set
        {
            if (SetProperty(ref _analogChannelCount, value)) {
                OnPropertyChanged(nameof(SignalSummary));
            }
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetProperty(ref _durationSeconds, value)) {
                OnPropertyChanged(nameof(DurationSummary));
            }
        }
    }

    public bool HasSession => SignalCount > 0;

    public bool IsDemoDeviceLoaded
    {
        get => _isDemoDeviceLoaded;
        private set
        {
            if (SetProperty(ref _isDemoDeviceLoaded, value)) {
                OnPropertyChanged(nameof(CanAcquireSelectedDevice));
                OnPropertyChanged(nameof(CanLoadSelectedDevice));
                OnPropertyChanged(nameof(IsSelectedDeviceLoaded));
            }
        }
    }

    public bool IsAcquiring
    {
        get => _isAcquiring;
        private set
        {
            if (SetProperty(ref _isAcquiring, value)) {
                OnPropertyChanged(nameof(AcquisitionSummary));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public double ViewportStartSeconds
    {
        get => _viewportStartSeconds;
        private set
        {
            if (SetProperty(ref _viewportStartSeconds, value)) {
                OnPropertyChanged(nameof(ViewportSummary));
            }
        }
    }

    public double SecondsPerPixel
    {
        get => _secondsPerPixel;
        private set
        {
            if (SetProperty(ref _secondsPerPixel, value)) {
                OnPropertyChanged(nameof(ViewportSummary));
            }
        }
    }

    public string ViewportSummary => $"{ViewportStartSeconds * 1000.0:0.###} ms @ {SecondsPerPixel * 1_000_000.0:0.###} us/px";

    public string DurationSummary => DurationSeconds > 0.0
        ? $"Duration: {DurationSeconds * 1000.0:0.###} ms"
        : "Duration: n/a";

    public string SignalSummary => $"Signals: {SignalCount} ({DigitalChannelCount} digital, {AnalogChannelCount} analog)";

    public string AcquisitionSummary => IsAcquiring ? "Acquisition: running" : "Acquisition: stopped";

    public string DeviceStatusSummary => $"Device status: {SelectedDeviceStatus}";

    public string DeviceTransportSummary => _deviceConnection?.Summary ?? "Transport: not connected";

    public string CaptureSettingsSummary
    {
        get
        {
            var seconds = SelectedSampleCount.Samples / (double)SelectedSampleRate.SamplesPerSecond;
            return $"{SelectedSampleRate.Label}, {ActiveDigitalChannelCount} digital channels, {SelectedSampleCount.Label} samples ({seconds * 1000.0:0.###} ms), {TriggerSummary}";
        }
    }

    public string DecodeSummary => DecoderRowCount == 0
        ? "Decoder: off"
        : $"Decoders: {DecoderRowCount}, {DecodeAnnotations.Count} annotations";

    private string DecoderFrameFormat => $"{DecoderDataBits}{ParityCode}{DecoderStopBits}";

    private string ParityCode => DecoderParity switch
    {
        "Odd" => "O",
        "Even" => "E",
        "Mark" => "M",
        "Space" => "S",
        _ => "N",
    };

    private string SelectedDeviceStatus
    {
        get
        {
            if (SelectedDevice.CanAcquire) {
                return "ready";
            }

            return IsDisconnectedStatus(SelectedDevice.Status) ? "disconnected" : "unknown";
        }
    }

    public string TriggerSummary => SelectedTriggerEdge.Kind == TriggerEdgeKind.None
        ? "trigger off"
        : $"{SelectedTriggerEdge.Label} on D{TriggerChannelIndex}";

    public void RefreshDevices()
    {
        var scannedDevices = _hardwareDeviceProbe.ScanDevices()
            .Where(device => device.Kind is CaptureDeviceKind.SLogicCombo8Logic or CaptureDeviceKind.UnknownUsb)
            .Select(ProbeDeviceReadiness);
        var nextDevices = new List<CaptureDeviceOption> { DemoDevice };
        nextDevices.AddRange(DeduplicateCaptureDevices(scannedDevices));
        _deviceOptions = nextDevices.ToArray();
        OnPropertyChanged(nameof(DeviceOptions));

        var matchingSelection = _deviceOptions.FirstOrDefault(device => StringComparer.OrdinalIgnoreCase.Equals(device.Id, SelectedDevice.Id));
        SelectedDevice = matchingSelection ?? DemoDevice;
        ResetLoadedDeviceState();
        StatusMessage = SelectedDevice.Status;
    }

    private static IEnumerable<CaptureDeviceOption> DeduplicateCaptureDevices(IEnumerable<CaptureDeviceOption> devices)
    {
        return devices
            .GroupBy(GetDeviceIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(device => device.DeviceInterfaceGuid is not null)
                .First());
    }

    private static string GetDeviceIdentityKey(CaptureDeviceOption device)
    {
        return device.Kind == CaptureDeviceKind.SLogicCombo8Logic
            ? "slogic-combo-8-logic"
            : device.Id;
    }

    public void SelectDevice(CaptureDeviceOption? device)
    {
        if (device is null) {
            return;
        }

        var normalizedDevice = NormalizeCaptureDevice(device);
        var nextDevice = _deviceOptions.FirstOrDefault(option => StringComparer.OrdinalIgnoreCase.Equals(option.Id, normalizedDevice.Id))
            ?? normalizedDevice;
        if (!StringComparer.OrdinalIgnoreCase.Equals(SelectedDevice.Id, nextDevice.Id)) {
            ResetLoadedDeviceState();
        }

        SelectedDevice = nextDevice;
        StatusMessage = SelectedDevice.Status;
    }

    public void ConnectSelectedDevice()
    {
        StopAcquisition();
        if (!SelectedDevice.CanAcquire) {
            StatusMessage = SelectedDevice.Status;
            return;
        }

        if (SelectedDevice.IsDemo) {
            LoadDemoDevice();
            return;
        }

        _deviceConnection = _hardwareTransportInspector.Inspect(SelectedDevice);
        OnPropertyChanged(nameof(DeviceTransportSummary));
        OnPropertyChanged(nameof(DeviceConnectionDetails));
        if (!_deviceConnection.IsConnected) {
            var failedDevice = SelectedDevice with { CanAcquire = false, Status = ClassifyConnectionStatus(_deviceConnection.Summary) };
            _deviceOptions = _deviceOptions
                .Select(device => StringComparer.OrdinalIgnoreCase.Equals(device.Id, failedDevice.Id) ? failedDevice : device)
                .ToArray();
            OnPropertyChanged(nameof(DeviceOptions));
            SelectedDevice = failedDevice;
            StatusMessage = _deviceConnection.Summary;
            return;
        }

        IsDemoDeviceLoaded = false;
        SetHardwareDeviceLoaded(true);
        CurrentFilePath = SelectedDevice.DisplayName;
        SignalCount = ActiveDigitalChannelCount;
        DigitalChannelCount = ActiveDigitalChannelCount;
        AnalogChannelCount = 0;
        DurationSeconds = 0.0;
        _hardwareCapturedDurationSeconds = 0.0;
        _hardwareCaptureStopwatch.Reset();
        ViewportStartSeconds = 0.0;
        ClearDecodeAnnotations();
        StatusMessage = _deviceConnection.Summary;
    }

    public void DisconnectSelectedDevice()
    {
        if (!IsSelectedDeviceLoaded) {
            return;
        }

        ResetLoadedDeviceState();
        StatusMessage = "Device disconnected";
    }

    public void SetActiveDigitalChannelCount(int channelCount)
    {
        var nextChannelCount = ChannelCountChoices.Contains(channelCount)
            ? channelCount
            : ChannelCountChoices.OrderBy(choice => Math.Abs(choice - channelCount)).First();
        if (_activeDigitalChannelCount == nextChannelCount) {
            return;
        }

        _activeDigitalChannelCount = nextChannelCount;
        _demoCapture.Configure(_activeDigitalChannelCount, SelectedSampleRate.SamplesPerSecond);
        UpdateAvailableSampleRates();
        UpdateDecoderChannelOptions();
        if (IsDemoDeviceLoaded) {
            DigitalChannelCount = _demoCapture.DigitalChannelCount;
            SignalCount = _demoCapture.SignalCount;
        }
        else if (_isHardwareDeviceLoaded) {
            DigitalChannelCount = ActiveDigitalChannelCount;
            SignalCount = ActiveDigitalChannelCount;
        }

        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(ActiveDigitalChannelCount));
        OnPropertyChanged(nameof(CaptureSettingsSummary));
    }

    public void SetSampleRate(CaptureSampleRateOption? sampleRate)
    {
        if (sampleRate is null || !_availableSampleRates.Contains(sampleRate)) {
            return;
        }

        if (_selectedSampleRate == sampleRate) {
            return;
        }

        _selectedSampleRate = sampleRate;
        _demoCapture.Configure(_activeDigitalChannelCount, _selectedSampleRate.SamplesPerSecond);
        OnPropertyChanged(nameof(SelectedSampleRate));
        OnPropertyChanged(nameof(CaptureSettingsSummary));
    }

    public void SetSampleCount(CaptureSampleCountOption? sampleCount)
    {
        if (sampleCount is null || !AllSampleCounts.Contains(sampleCount) || _selectedSampleCount == sampleCount) {
            return;
        }

        _selectedSampleCount = sampleCount;
        OnPropertyChanged(nameof(SelectedSampleCount));
        OnPropertyChanged(nameof(CaptureSettingsSummary));
    }

    public void SetTriggerEdge(TriggerEdgeOption? triggerEdge)
    {
        if (triggerEdge is null || !AllTriggerEdges.Contains(triggerEdge) || _selectedTriggerEdge == triggerEdge) {
            return;
        }

        _selectedTriggerEdge = triggerEdge;
        OnPropertyChanged(nameof(SelectedTriggerEdge));
        OnPropertyChanged(nameof(TriggerSummary));
        OnPropertyChanged(nameof(CaptureSettingsSummary));
    }

    public void SetTriggerChannelIndex(int channelIndex)
    {
        var nextChannelIndex = Math.Clamp(channelIndex, 0, Math.Max(0, ActiveDigitalChannelCount - 1));
        if (_triggerChannelIndex == nextChannelIndex) {
            return;
        }

        _triggerChannelIndex = nextChannelIndex;
        OnPropertyChanged(nameof(TriggerChannelIndex));
        OnPropertyChanged(nameof(TriggerSummary));
        OnPropertyChanged(nameof(CaptureSettingsSummary));
    }

    public void SetDecoder(ProtocolDecoderOption? decoder)
    {
        if (decoder is null || !AllDecoders.Contains(decoder) || _selectedDecoder == decoder) {
            return;
        }

        _selectedDecoder = decoder;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(SelectedDecoder));
        OnPropertyChanged(nameof(DecoderRowCount));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void SetDecoderChannelIndex(int channelIndex)
    {
        var nextChannelIndex = Math.Clamp(channelIndex, 0, Math.Max(0, ActiveDigitalChannelCount - 1));
        if (_decoderChannelIndex == nextChannelIndex) {
            return;
        }

        _decoderChannelIndex = nextChannelIndex;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderChannelIndex));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void SetDecoderBaudRate(int baudRate)
    {
        if (!BaudRateChoices.Contains(baudRate) || _decoderBaudRate == baudRate) {
            return;
        }

        _decoderBaudRate = baudRate;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderBaudRate));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void SetDecoderDataBits(int dataBits)
    {
        if (!DataBitChoices.Contains(dataBits) || _decoderDataBits == dataBits) {
            return;
        }

        _decoderDataBits = dataBits;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderDataBits));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void SetDecoderStopBits(string? stopBits)
    {
        if (stopBits is null || !StopBitChoices.Contains(stopBits) || _decoderStopBits == stopBits) {
            return;
        }

        _decoderStopBits = stopBits;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderStopBits));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void SetDecoderParity(string? parity)
    {
        if (parity is null || !ParityChoices.Contains(parity) || _decoderParity == parity) {
            return;
        }

        _decoderParity = parity;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderParity));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void SetDecoderColor(DecoderColorOption? color)
    {
        if (color is null || !AllDecoderColors.Contains(color) || _decoderColor == color) {
            return;
        }

        _decoderColor = color;
        UpdateEditingDecoder();
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderColor));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void AddConfiguredDecoder()
    {
        if (SelectedDecoder.Kind == ProtocolDecoderKind.None) {
            return;
        }

        _configuredDecoders = [
            .. _configuredDecoders,
            CreateConfiguredDecoder(_nextDecoderId++),
        ];
        _editingDecoderId = null;
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(ConfiguredDecoders));
        OnPropertyChanged(nameof(DecoderRowCount));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void EditConfiguredDecoder(int decoderId)
    {
        var decoder = _configuredDecoders.FirstOrDefault(item => item.Id == decoderId);
        if (decoder is null) {
            return;
        }

        _editingDecoderId = decoder.Id;
        _selectedDecoder = decoder.Protocol;
        _decoderChannelIndex = Math.Clamp(decoder.ChannelIndex, 0, Math.Max(0, ActiveDigitalChannelCount - 1));
        _decoderBaudRate = decoder.BaudRate;
        _decoderDataBits = decoder.DataBits;
        _decoderStopBits = decoder.StopBits;
        _decoderParity = decoder.Parity;
        _decoderColor = decoder.Color;
        OnPropertyChanged(nameof(SelectedDecoder));
        OnPropertyChanged(nameof(DecoderChannelIndex));
        OnPropertyChanged(nameof(DecoderBaudRate));
        OnPropertyChanged(nameof(DecoderDataBits));
        OnPropertyChanged(nameof(DecoderStopBits));
        OnPropertyChanged(nameof(DecoderParity));
        OnPropertyChanged(nameof(DecoderColor));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public void RemoveConfiguredDecoder(int decoderId)
    {
        var nextDecoders = _configuredDecoders.Where(decoder => decoder.Id != decoderId).ToArray();
        if (nextDecoders.Length == _configuredDecoders.Length) {
            return;
        }

        _configuredDecoders = nextDecoders;
        if (_editingDecoderId == decoderId) {
            _editingDecoderId = null;
        }

        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(ConfiguredDecoders));
        OnPropertyChanged(nameof(DecoderRowCount));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public async Task OpenFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (IsBusy) {
            return;
        }

        IsBusy = true;
        StatusMessage = "Opening capture...";

        try {
            StopAcquisition();
            var session = await _sessionService.OpenFileAsync(path, cancellationToken).ConfigureAwait(true);
            IsDemoDeviceLoaded = false;
            SetHardwareDeviceLoaded(false);
            ClearDeviceConnection();
            CurrentFilePath = session.FilePath;
            SignalCount = session.SignalCount;
            DigitalChannelCount = session.SignalCount;
            AnalogChannelCount = 0;
            DurationSeconds = session.DurationSeconds;
            ViewportStartSeconds = 0.0;
            ClearDecodeAnnotations();
            StatusMessage = "Capture opened";
        }
        catch (Exception ex) when (ex is NativeException or ArgumentException or IOException) {
            StatusMessage = ex.Message;
            throw;
        }
        finally {
            IsBusy = false;
        }
    }

    public void LoadDemoDevice()
    {
        _demoCapture.Configure(ActiveDigitalChannelCount, SelectedSampleRate.SamplesPerSecond);
        _demoCapture.Reset();
        IsDemoDeviceLoaded = true;
        SetHardwareDeviceLoaded(false);
        _hardwareCapturedDurationSeconds = 0.0;
        _hardwareCaptureStopwatch.Reset();
        IsAcquiring = false;
        ClearDeviceConnection();
        CurrentFilePath = "Demo Device";
        SignalCount = _demoCapture.SignalCount;
        DigitalChannelCount = _demoCapture.DigitalChannelCount;
        AnalogChannelCount = _demoCapture.AnalogChannelCount;
        DurationSeconds = 0.0;
        ViewportStartSeconds = 0.0;
        SecondsPerPixel = DefaultSecondsPerPixel;
        ClearDecodeAnnotations();
        StatusMessage = "Demo device ready";
        OnPropertyChanged(nameof(AcquisitionSummary));
    }

    public void StartDemoCapture()
    {
        StartAcquisition();
    }

    public void StopDemoCapture()
    {
        StopAcquisition();
    }

    public void StartAcquisition()
    {
        if (!IsDemoDeviceLoaded) {
            if (SelectedDevice.IsDemo) {
                LoadDemoDevice();
            }
            else {
                StartHardwareAcquisition();
                return;
            }
        }

        _demoCapture.Start();
        DurationSeconds = 0.0;
        ViewportStartSeconds = 0.0;
        ClearDecodeAnnotations();
        IsAcquiring = true;
        StatusMessage = "Demo acquisition running";
        OnPropertyChanged(nameof(AcquisitionSummary));
    }

    public void StopAcquisition()
    {
        if (!IsAcquiring) {
            return;
        }

        if (IsDemoDeviceLoaded) {
            _demoCapture.Stop();
            DurationSeconds = _demoCapture.DurationSeconds;
        }
        else if (_isHardwareDeviceLoaded) {
            _hardwareCaptureStopwatch.Stop();
            _hardwareCapturedDurationSeconds = Math.Max(
                _hardwareCapturedDurationSeconds,
                _hardwareCaptureStopwatch.Elapsed.TotalSeconds);
            DurationSeconds = _hardwareCapturedDurationSeconds;
        }

        IsAcquiring = false;
        RebuildDecodeAnnotations();
        StatusMessage = IsDemoDeviceLoaded ? "Demo acquisition stopped" : $"{SelectedDevice.DisplayName} acquisition stopped";
        OnPropertyChanged(nameof(AcquisitionSummary));
    }

    public void RefreshAcquisition()
    {
        if (!IsAcquiring) {
            return;
        }

        if (IsDemoDeviceLoaded) {
            var targetDurationSeconds = SelectedSampleCount.Samples / (double)SelectedSampleRate.SamplesPerSecond;
            var currentDurationSeconds = _demoCapture.DurationSeconds;
            if (currentDurationSeconds + CaptureCompletionToleranceSeconds >= targetDurationSeconds) {
                _demoCapture.StopAt(targetDurationSeconds);
                DurationSeconds = targetDurationSeconds;
                IsAcquiring = false;
                RebuildDecodeAnnotations();
                StatusMessage = "Demo acquisition complete";
                OnPropertyChanged(nameof(AcquisitionSummary));
                return;
            }

            DurationSeconds = currentDurationSeconds;
            RebuildDecodeAnnotations();
            return;
        }

        if (_isHardwareDeviceLoaded && _hardwareCaptureStopwatch.IsRunning) {
            var targetDurationSeconds = SelectedSampleCount.Samples / (double)SelectedSampleRate.SamplesPerSecond;
            var currentDurationSeconds = _hardwareCaptureStopwatch.Elapsed.TotalSeconds;
            if (currentDurationSeconds + CaptureCompletionToleranceSeconds >= targetDurationSeconds) {
                _hardwareCaptureStopwatch.Stop();
                _hardwareCapturedDurationSeconds = targetDurationSeconds;
                DurationSeconds = targetDurationSeconds;
                IsAcquiring = false;
                RebuildDecodeAnnotations();
                StatusMessage = $"{SelectedDevice.DisplayName} acquisition complete";
                OnPropertyChanged(nameof(AcquisitionSummary));
                return;
            }

            _hardwareCapturedDurationSeconds = currentDurationSeconds;
            DurationSeconds = _hardwareCapturedDurationSeconds;
        }

        RebuildDecodeAnnotations();
    }

    public void PanLeft()
    {
        PanByPixels(-96);
    }

    public void PanRight()
    {
        PanByPixels(96);
    }

    public void ZoomIn()
    {
        ZoomAtPixel(160.0, -1);
    }

    public void ZoomOut()
    {
        ZoomAtPixel(160.0, 1);
    }

    public void ResetViewport(float widthPixels = 320.0F)
    {
        ViewportStartSeconds = 0.0;
        if (widthPixels > 0.0F && DurationSeconds > 0.0) {
            SecondsPerPixel = Math.Clamp(DurationSeconds / widthPixels, MinimumSecondsPerPixel, MaximumSecondsPerPixel);
        }
        else {
            SecondsPerPixel = DefaultSecondsPerPixel;
        }
    }

    public void PanByPixels(double pixels)
    {
        ViewportStartSeconds = Math.Max(0.0, ViewportStartSeconds + pixels * SecondsPerPixel);
    }

    public void ZoomAtPixel(double pivotPixel, int stepCount)
    {
        ZoomAtPixel(pivotPixel, stepCount, double.NaN);
    }

    public void ZoomAtPixel(double pivotPixel, int stepCount, double viewportWidthPixels)
    {
        if (!double.IsFinite(pivotPixel) || stepCount == 0) {
            return;
        }

        var clampedPivotPixel = Math.Max(0.0, pivotPixel);
        if (double.IsFinite(viewportWidthPixels) && viewportWidthPixels > 0.0) {
            clampedPivotPixel = Math.Min(clampedPivotPixel, viewportWidthPixels);
        }

        var pivotSeconds = ViewportStartSeconds + clampedPivotPixel * SecondsPerPixel;
        var newSecondsPerPixel = Math.Clamp(
            ViewportScaleLadder.Step(SecondsPerPixel, stepCount),
            MinimumSecondsPerPixel,
            MaximumSecondsPerPixel);

        var newViewportStartSeconds = Math.Max(0.0, pivotSeconds - clampedPivotPixel * newSecondsPerPixel);
        if (TryGetRightBlankZoomStart(clampedPivotPixel, stepCount, newSecondsPerPixel, out var rightBlankStartSeconds)) {
            newViewportStartSeconds = rightBlankStartSeconds;
        }

        SecondsPerPixel = newSecondsPerPixel;
        ViewportStartSeconds = newViewportStartSeconds;
    }

    private bool TryGetRightBlankZoomStart(
        double pivotPixel,
        int stepCount,
        double newSecondsPerPixel,
        out double newViewportStartSeconds)
    {
        newViewportStartSeconds = 0.0;
        if (stepCount >= 0 || DurationSeconds <= 0.0 || pivotPixel <= 0.0) {
            return false;
        }

        var currentEndPixel = (DurationSeconds - ViewportStartSeconds) / SecondsPerPixel;
        if (!double.IsFinite(currentEndPixel) || currentEndPixel < 0.0 || currentEndPixel >= pivotPixel) {
            return false;
        }

        var fixedStartEndPixel = (DurationSeconds - ViewportStartSeconds) / newSecondsPerPixel;
        newViewportStartSeconds = fixedStartEndPixel <= pivotPixel
            ? ViewportStartSeconds
            : Math.Max(0.0, DurationSeconds - pivotPixel * newSecondsPerPixel);
        return true;
    }

    private void UpdateAvailableSampleRates()
    {
        var maxRate = ActiveDigitalChannelCount switch
        {
            <= 2 => 80_000_000,
            <= 4 => 40_000_000,
            _ => 20_000_000,
        };

        _availableSampleRates = AllSampleRates.Where(rate => rate.SamplesPerSecond <= maxRate).ToArray();
        if (!_availableSampleRates.Contains(_selectedSampleRate)) {
            _selectedSampleRate = _availableSampleRates[^1];
            _demoCapture.Configure(_activeDigitalChannelCount, _selectedSampleRate.SamplesPerSecond);
            OnPropertyChanged(nameof(SelectedSampleRate));
        }

        OnPropertyChanged(nameof(AvailableSampleRates));
        OnPropertyChanged(nameof(CaptureSettingsSummary));
    }

    private void UpdateDecoderChannelOptions()
    {
        _decoderChannelOptions = Enumerable.Range(0, ActiveDigitalChannelCount).Select(channel => $"D{channel}").ToArray();
        if (_triggerChannelIndex >= ActiveDigitalChannelCount) {
            _triggerChannelIndex = Math.Max(0, ActiveDigitalChannelCount - 1);
            OnPropertyChanged(nameof(TriggerChannelIndex));
            OnPropertyChanged(nameof(TriggerSummary));
        }

        if (_decoderChannelIndex >= ActiveDigitalChannelCount) {
            _decoderChannelIndex = Math.Max(0, ActiveDigitalChannelCount - 1);
            OnPropertyChanged(nameof(DecoderChannelIndex));
        }

        var maxChannelIndex = Math.Max(0, ActiveDigitalChannelCount - 1);
        var nextDecoders = _configuredDecoders
            .Select(decoder => decoder.ChannelIndex > maxChannelIndex ? decoder with { ChannelIndex = maxChannelIndex } : decoder)
            .ToArray();
        if (!nextDecoders.SequenceEqual(_configuredDecoders)) {
            _configuredDecoders = nextDecoders;
            OnPropertyChanged(nameof(ConfiguredDecoders));
            RebuildDecodeAnnotations();
        }

        OnPropertyChanged(nameof(DecoderChannelOptions));
    }

    private void ClearDecodeAnnotations()
    {
        if (_decodeAnnotations.Length == 0) {
            return;
        }

        _decodeAnnotations = [];
        OnPropertyChanged(nameof(DecodeAnnotations));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    private void ClearDeviceConnection()
    {
        SetHardwareDeviceLoaded(false);
        _hardwareCapturedDurationSeconds = 0.0;
        _hardwareCaptureStopwatch.Reset();
        if (_deviceConnection is null) {
            return;
        }

        _deviceConnection = null;
        OnPropertyChanged(nameof(DeviceTransportSummary));
        OnPropertyChanged(nameof(DeviceConnectionDetails));
    }

    private void ResetLoadedDeviceState()
    {
        StopAcquisition();
        _demoCapture.Reset();
        IsDemoDeviceLoaded = false;
        SetHardwareDeviceLoaded(false);
        ClearDeviceConnection();
        CurrentFilePath = string.Empty;
        SignalCount = 0;
        DigitalChannelCount = 1;
        AnalogChannelCount = 0;
        DurationSeconds = 0.0;
        ViewportStartSeconds = 0.0;
        SecondsPerPixel = DefaultSecondsPerPixel;
        ClearDecodeAnnotations();
        OnPropertyChanged(nameof(AcquisitionSummary));
    }

    private ConfiguredDecoder CreateConfiguredDecoder(int id)
    {
        return new ConfiguredDecoder(
            id,
            SelectedDecoder,
            DecoderChannelIndex,
            DecoderBaudRate,
            DecoderDataBits,
            DecoderStopBits,
            DecoderParity,
            DecoderColor);
    }

    private void UpdateEditingDecoder()
    {
        if (_editingDecoderId is not { } decoderId) {
            return;
        }

        var nextDecoder = CreateConfiguredDecoder(decoderId);
        var updated = false;
        _configuredDecoders = _configuredDecoders
            .Select(decoder =>
            {
                if (decoder.Id != decoderId) {
                    return decoder;
                }

                updated = true;
                return nextDecoder;
            })
            .ToArray();
        if (updated) {
            OnPropertyChanged(nameof(ConfiguredDecoders));
            OnPropertyChanged(nameof(DecoderRowCount));
        }
    }

    private void SetHardwareDeviceLoaded(bool isLoaded)
    {
        if (_isHardwareDeviceLoaded == isLoaded) {
            return;
        }

        _isHardwareDeviceLoaded = isLoaded;
        OnPropertyChanged(nameof(CanAcquireSelectedDevice));
        OnPropertyChanged(nameof(CanLoadSelectedDevice));
        OnPropertyChanged(nameof(IsSelectedDeviceLoaded));
    }

    private static CaptureDeviceOption NormalizeCaptureDevice(CaptureDeviceOption device)
    {
        return device.Kind == CaptureDeviceKind.SLogicCombo8Logic
            ? device with { DisplayName = "SLogic Combo 8", DriverName = "SLogic Combo 8" }
            : device;
    }

    private CaptureDeviceOption ProbeDeviceReadiness(CaptureDeviceOption device)
    {
        var normalizedDevice = NormalizeCaptureDevice(device);
        if (normalizedDevice.Kind != CaptureDeviceKind.SLogicCombo8Logic) {
            return normalizedDevice;
        }

        var connection = _hardwareTransportInspector.Inspect(normalizedDevice);
        return connection.IsConnected
            ? normalizedDevice with { CanAcquire = true, Status = "Ready" }
            : normalizedDevice with { CanAcquire = false, Status = ClassifyConnectionStatus(connection.Summary) };
    }

    private static string ClassifyConnectionStatus(string status)
    {
        return IsDisconnectedStatus(status) ? "Disconnected" : "Unknown";
    }

    private static bool IsDisconnectedStatus(string status)
    {
        return status.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || status.Contains("not detected", StringComparison.OrdinalIgnoreCase)
            || status.Contains("No WinUSB device interface path", StringComparison.OrdinalIgnoreCase)
            || status.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private void StartHardwareAcquisition()
    {
        if (!_isHardwareDeviceLoaded) {
            ConnectSelectedDevice();
        }

        if (!_isHardwareDeviceLoaded) {
            StatusMessage = "Load a supported capture device first.";
            return;
        }

        if (_deviceConnection is { IsConnected: false }) {
            StatusMessage = _deviceConnection.Summary;
            return;
        }

        _hardwareCapturedDurationSeconds = 0.0;
        _hardwareCaptureStopwatch.Restart();
        DurationSeconds = 0.0;
        ViewportStartSeconds = 0.0;
        ClearDecodeAnnotations();
        IsAcquiring = true;
        StatusMessage = $"{SelectedDevice.DisplayName} acquisition running";
    }

    private void RebuildDecodeAnnotations()
    {
        var annotations = new List<DecodedAnnotation>();
        if (IsDemoDeviceLoaded && DurationSeconds > 0.0) {
            foreach (var decoder in _configuredDecoders.Where(IsDemoUartDecoderCompatible)) {
                annotations.AddRange(_demoCapture.QueryUartAnnotations(
                    0.0,
                    DurationSeconds,
                    decoder.ChannelIndex,
                    decoder.BaudRate,
                    maxAnnotations: MaximumCachedDecodeAnnotations));
            }
        }

        var nextAnnotations = annotations.ToArray();
        if (_decodeAnnotations.SequenceEqual(nextAnnotations)) {
            return;
        }

        _decodeAnnotations = nextAnnotations;
        OnPropertyChanged(nameof(DecodeAnnotations));
        OnPropertyChanged(nameof(DecodeSummary));
    }

    public NativeDigitalSpan[] QueryDigitalSpans(float widthPixels)
    {
        if (widthPixels <= 0.0F) {
            return [];
        }

        if (IsDemoDeviceLoaded) {
            return _demoCapture.QueryDigitalSpans(ViewportStartSeconds, SecondsPerPixel, widthPixels);
        }

        if (_isHardwareDeviceLoaded) {
            return QueryHardwareDigitalSpans(widthPixels);
        }

        return _sessionService.QueryDigitalSpans(ViewportStartSeconds, SecondsPerPixel, widthPixels);
    }

    private NativeDigitalSpan[] QueryHardwareDigitalSpans(float widthPixels)
    {
        var channelCount = Math.Max(0, DigitalChannelCount);
        if (channelCount == 0 || SecondsPerPixel <= 0.0) {
            return [];
        }

        var captureEndSeconds = _hardwareCaptureStopwatch.IsRunning
            ? Math.Max(DurationSeconds, _hardwareCaptureStopwatch.Elapsed.TotalSeconds)
            : DurationSeconds;
        if (captureEndSeconds <= ViewportStartSeconds) {
            return [];
        }

        var viewportEndSeconds = ViewportStartSeconds + SecondsPerPixel * widthPixels;
        var visibleEndSeconds = Math.Min(captureEndSeconds, viewportEndSeconds);
        if (visibleEndSeconds <= ViewportStartSeconds) {
            return [];
        }

        var maxVisibleX = (float)Math.Clamp((visibleEndSeconds - ViewportStartSeconds) / SecondsPerPixel, 0.0, widthPixels);
        if (maxVisibleX <= 0.0F) {
            return [];
        }

        var spans = new List<NativeDigitalSpan>(channelCount);
        for (var channel = 0; channel < channelCount; channel++) {
            spans.Add(new NativeDigitalSpan(0.0F, maxVisibleX, 0, 0, (byte)channel));
        }

        var pointSpacingPixels = 1.0 / (SelectedSampleRate.SamplesPerSecond * SecondsPerPixel);
        if (double.IsFinite(pointSpacingPixels) && pointSpacingPixels >= HardwareDigitalPointMinimumPixelSpacing) {
            var firstSampleIndex = Math.Max(0L, (long)Math.Ceiling(ViewportStartSeconds * SelectedSampleRate.SamplesPerSecond));
            var lastSampleIndex = Math.Max(firstSampleIndex - 1, (long)Math.Floor(visibleEndSeconds * SelectedSampleRate.SamplesPerSecond));
            for (var sampleIndex = firstSampleIndex; sampleIndex <= lastSampleIndex; sampleIndex++) {
                var sampleSeconds = sampleIndex / (double)SelectedSampleRate.SamplesPerSecond;
                var x = (float)Math.Clamp((sampleSeconds - ViewportStartSeconds) / SecondsPerPixel, 0.0, widthPixels);
                for (var channel = 0; channel < channelCount; channel++) {
                    spans.Add(new NativeDigitalSpan(x, x, 0, DigitalPointFlag, (byte)channel));
                }
            }
        }

        return spans.ToArray();
    }

    public NativeAnalogSegment[] QueryAnalogSegments(float widthPixels)
    {
        if (widthPixels <= 0.0F || !IsDemoDeviceLoaded) {
            return [];
        }

        return _demoCapture.QueryAnalogSegments(ViewportStartSeconds, SecondsPerPixel, widthPixels);
    }

    public NativeDecoderAnnotation[] QueryDecoderAnnotations(float widthPixels)
    {
        if (widthPixels <= 0.0F || DecoderRowCount == 0) {
            return [];
        }

        var viewportEndSeconds = ViewportStartSeconds + SecondsPerPixel * widthPixels;
        var visibleEndSeconds = DurationSeconds > 0.0
            ? Math.Min(DurationSeconds, viewportEndSeconds)
            : viewportEndSeconds;
        if (visibleEndSeconds <= ViewportStartSeconds) {
            return [];
        }

        if (IsDemoDeviceLoaded && DurationSeconds > 0.0) {
            var nativeAnnotations = new List<NativeDecoderAnnotation>();
            for (var row = 0; row < _configuredDecoders.Length; row++) {
                var decoder = _configuredDecoders[row];
                if (!IsDemoUartDecoderCompatible(decoder)) {
                    continue;
                }

                var annotations = _demoCapture.QueryUartAnnotations(
                    ViewportStartSeconds,
                    visibleEndSeconds,
                    decoder.ChannelIndex,
                    decoder.BaudRate,
                    MaximumVisibleDecodeAnnotations + 1).ToArray();
                if (annotations.Length > MaximumVisibleDecodeAnnotations) {
                    nativeAnnotations.Add(CreateNativeDecoderAnnotation(
                        annotations[0].StartSeconds,
                        visibleEndSeconds,
                        widthPixels,
                        string.Empty,
                        (byte)row,
                        decoder.Color.Rgb));
                    continue;
                }

                nativeAnnotations.AddRange(annotations
                    .Select(annotation => CreateNativeDecoderAnnotation(
                        annotation.StartSeconds,
                        annotation.EndSeconds,
                        widthPixels,
                        annotation.Text,
                        (byte)row,
                        decoder.Color.Rgb))
                    .Where(annotation => annotation.X1 > annotation.X0));
            }

            return nativeAnnotations.ToArray();
        }

        return _decodeAnnotations
            .Where(annotation => annotation.EndSeconds >= ViewportStartSeconds && annotation.StartSeconds <= visibleEndSeconds)
            .Select(annotation => CreateNativeDecoderAnnotation(
                annotation.StartSeconds,
                annotation.EndSeconds,
                widthPixels,
                annotation.Text,
                0,
                AllDecoderColors[0].Rgb))
            .Where(annotation => annotation.X1 > annotation.X0)
            .ToArray();
    }

    private static bool IsDemoUartDecoderCompatible(ConfiguredDecoder decoder)
    {
        return decoder.Protocol.Kind == ProtocolDecoderKind.Uart
            && decoder.DataBits == 8
            && decoder.StopBits == "1"
            && decoder.Parity == "None";
    }

    private NativeDecoderAnnotation CreateNativeDecoderAnnotation(
        double startSeconds,
        double endSeconds,
        float widthPixels,
        string text,
        byte rowIndex,
        uint colorRgb)
    {
        var clippedStartSeconds = Math.Clamp(startSeconds, ViewportStartSeconds, ViewportStartSeconds + SecondsPerPixel * widthPixels);
        var clippedEndSeconds = Math.Clamp(
            DurationSeconds > 0.0 ? Math.Min(endSeconds, DurationSeconds) : endSeconds,
            ViewportStartSeconds,
            ViewportStartSeconds + SecondsPerPixel * widthPixels);
        var x0 = (float)((clippedStartSeconds - ViewportStartSeconds) / SecondsPerPixel);
        var x1 = (float)((clippedEndSeconds - ViewportStartSeconds) / SecondsPerPixel);
        return new NativeDecoderAnnotation(
            Math.Clamp(x0, 0.0F, widthPixels),
            Math.Clamp(x1, 0.0F, widthPixels),
            rowIndex,
            text,
            colorRgb);
    }

    public void Dispose()
    {
        _demoCapture.Stop();
        _hardwareCaptureStopwatch.Stop();
        _sessionService.Dispose();
    }
}

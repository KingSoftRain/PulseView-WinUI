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
        new(ProtocolDecoderKind.Uart, "UART 8N1"),
    ];

    private static readonly TriggerEdgeOption[] AllTriggerEdges = [
        new(TriggerEdgeKind.None, "No trigger"),
        new(TriggerEdgeKind.Rising, "Rising edge"),
        new(TriggerEdgeKind.Falling, "Falling edge"),
        new(TriggerEdgeKind.Either, "Either edge"),
    ];

    private static readonly int[] ChannelCountChoices = [1, 2, 4, 8];
    private static readonly int[] BaudRateChoices = [9_600, 57_600, 115_200, 1_000_000];

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
    private string[] _decoderChannelOptions = [];
    private DecodedAnnotation[] _decodeAnnotations = [];
    private CaptureDeviceConnection? _deviceConnection;
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
            }
        }
    }

    public IReadOnlyList<int> DigitalChannelCountOptions => ChannelCountChoices;

    public IReadOnlyList<CaptureSampleRateOption> AvailableSampleRates => _availableSampleRates;

    public IReadOnlyList<CaptureSampleCountOption> SampleCountOptions => AllSampleCounts;

    public IReadOnlyList<ProtocolDecoderOption> DecoderOptions => AllDecoders;

    public IReadOnlyList<TriggerEdgeOption> TriggerEdgeOptions => AllTriggerEdges;

    public IReadOnlyList<string> DecoderChannelOptions => _decoderChannelOptions;

    public IReadOnlyList<int> DecoderBaudRateOptions => BaudRateChoices;

    public CaptureSampleRateOption SelectedSampleRate => _selectedSampleRate;

    public CaptureSampleCountOption SelectedSampleCount => _selectedSampleCount;

    public ProtocolDecoderOption SelectedDecoder => _selectedDecoder;

    public TriggerEdgeOption SelectedTriggerEdge => _selectedTriggerEdge;

    public int ActiveDigitalChannelCount => _activeDigitalChannelCount;

    public int TriggerChannelIndex => _triggerChannelIndex;

    public int DecoderChannelIndex => _decoderChannelIndex;

    public int DecoderBaudRate => _decoderBaudRate;

    public IReadOnlyList<DecodedAnnotation> DecodeAnnotations => _decodeAnnotations;

    public IReadOnlyList<string> DeviceConnectionDetails => _deviceConnection?.Details ?? [];

    public bool CanAcquireSelectedDevice => SelectedDevice.IsDemo
        || (_isHardwareDeviceLoaded && SelectedDevice.Kind == CaptureDeviceKind.SLogicCombo8Logic);

    public int DecoderRowCount => SelectedDecoder.Kind == ProtocolDecoderKind.None ? 0 : 1;

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

    public string DeviceStatusSummary => $"{SelectedDevice.DriverName}: {SelectedDevice.Status}";

    public string DeviceTransportSummary => _deviceConnection?.Summary ?? "Transport: not connected";

    public string CaptureSettingsSummary
    {
        get
        {
            var seconds = SelectedSampleCount.Samples / (double)SelectedSampleRate.SamplesPerSecond;
            return $"{SelectedSampleRate.Label}, {ActiveDigitalChannelCount} digital channels, {SelectedSampleCount.Label} samples ({seconds * 1000.0:0.###} ms), {TriggerSummary}";
        }
    }

    public string DecodeSummary => SelectedDecoder.Kind == ProtocolDecoderKind.None
        ? "Decoder: off"
        : $"Decoder: {SelectedDecoder.Label} on D{DecoderChannelIndex}, {DecoderBaudRate} baud, {DecodeAnnotations.Count} annotations";

    public string TriggerSummary => SelectedTriggerEdge.Kind == TriggerEdgeKind.None
        ? "trigger off"
        : $"{SelectedTriggerEdge.Label} on D{TriggerChannelIndex}";

    public void RefreshDevices()
    {
        var scannedDevices = _hardwareDeviceProbe.ScanDevices()
            .Where(device => device.Kind == CaptureDeviceKind.SLogicCombo8Logic);
        var nextDevices = new List<CaptureDeviceOption> { DemoDevice };
        nextDevices.AddRange(DeduplicateCaptureDevices(scannedDevices));
        _deviceOptions = nextDevices.ToArray();
        OnPropertyChanged(nameof(DeviceOptions));

        var matchingSelection = _deviceOptions.FirstOrDefault(device => StringComparer.OrdinalIgnoreCase.Equals(device.Id, SelectedDevice.Id));
        SelectedDevice = matchingSelection ?? DemoDevice;
        ClearDeviceConnection();
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

        SelectedDevice = device;
        ClearDeviceConnection();
        StatusMessage = device.Status;
    }

    public void ConnectSelectedDevice()
    {
        StopAcquisition();
        if (SelectedDevice.IsDemo) {
            LoadDemoDevice();
            return;
        }

        _deviceConnection = _hardwareTransportInspector.Inspect(SelectedDevice);
        OnPropertyChanged(nameof(DeviceTransportSummary));
        OnPropertyChanged(nameof(DeviceConnectionDetails));
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
        RebuildDecodeAnnotations();
        OnPropertyChanged(nameof(DecoderBaudRate));
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
            _hardwareCapturedDurationSeconds = _hardwareCaptureStopwatch.Elapsed.TotalSeconds;
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

    private void SetHardwareDeviceLoaded(bool isLoaded)
    {
        if (_isHardwareDeviceLoaded == isLoaded) {
            return;
        }

        _isHardwareDeviceLoaded = isLoaded;
        OnPropertyChanged(nameof(CanAcquireSelectedDevice));
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
        DecodedAnnotation[] annotations = [];
        if (IsDemoDeviceLoaded && DurationSeconds > 0.0 && SelectedDecoder.Kind == ProtocolDecoderKind.Uart) {
            annotations = _demoCapture.QueryUartAnnotations(
                0.0,
                DurationSeconds,
                DecoderChannelIndex,
                DecoderBaudRate,
                maxAnnotations: MaximumCachedDecodeAnnotations).ToArray();
        }

        if (_decodeAnnotations.SequenceEqual(annotations)) {
            return;
        }

        _decodeAnnotations = annotations;
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
            return [];
        }

        return _sessionService.QueryDigitalSpans(ViewportStartSeconds, SecondsPerPixel, widthPixels);
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

        if (IsDemoDeviceLoaded && DurationSeconds > 0.0 && SelectedDecoder.Kind == ProtocolDecoderKind.Uart) {
            var annotations = _demoCapture.QueryUartAnnotations(
                ViewportStartSeconds,
                visibleEndSeconds,
                DecoderChannelIndex,
                DecoderBaudRate,
                MaximumVisibleDecodeAnnotations + 1).ToArray();
            if (annotations.Length > MaximumVisibleDecodeAnnotations) {
                return [
                    CreateNativeDecoderAnnotation(
                        annotations[0].StartSeconds,
                        visibleEndSeconds,
                        widthPixels,
                        string.Empty),
                ];
            }

            return annotations
                .Select(annotation => CreateNativeDecoderAnnotation(
                    annotation.StartSeconds,
                    annotation.EndSeconds,
                    widthPixels,
                    annotation.Text))
                .Where(annotation => annotation.X1 > annotation.X0)
                .ToArray();
        }

        return _decodeAnnotations
            .Where(annotation => annotation.EndSeconds >= ViewportStartSeconds && annotation.StartSeconds <= visibleEndSeconds)
            .Select(annotation => CreateNativeDecoderAnnotation(
                annotation.StartSeconds,
                annotation.EndSeconds,
                widthPixels,
                annotation.Text))
            .Where(annotation => annotation.X1 > annotation.X0)
            .ToArray();
    }

    private NativeDecoderAnnotation CreateNativeDecoderAnnotation(
        double startSeconds,
        double endSeconds,
        float widthPixels,
        string text)
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
            0,
            text);
    }

    public void Dispose()
    {
        _demoCapture.Stop();
        _hardwareCaptureStopwatch.Stop();
        _sessionService.Dispose();
    }
}

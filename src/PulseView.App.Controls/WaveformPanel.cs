using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PulseView.App.NativeInterop;
using Microsoft.UI.Text;

namespace PulseView.App.Controls;

public sealed class WaveformPanel : UserControl
{
    public const double PlotLeftMargin = 54.0;
    public const double PlotRightMargin = 18.0;
    private const double PlotTopMargin = 18.0;
    private const double PlotBottomMargin = 14.0;
    private const double DecoderTextInset = 5.0;

    public static readonly DependencyProperty ViewportStartSecondsProperty =
        DependencyProperty.Register(
            nameof(ViewportStartSeconds),
            typeof(double),
            typeof(WaveformPanel),
            new PropertyMetadata(0.0, OnViewportPropertyChanged));

    public static readonly DependencyProperty SecondsPerPixelProperty =
        DependencyProperty.Register(
            nameof(SecondsPerPixel),
            typeof(double),
            typeof(WaveformPanel),
            new PropertyMetadata(10.0e-6, OnViewportPropertyChanged));

    private readonly Grid _root;
    private readonly SwapChainPanel _swapChainPanel;
    private readonly Canvas _textOverlay;
    private readonly SolidColorBrush _trackLabelBrush = new(Windows.UI.Color.FromArgb(255, 226, 232, 240));
    private readonly SolidColorBrush _decoderTextBrush = new(Windows.UI.Color.FromArgb(255, 248, 250, 252));
    private NativeWaveformRenderer? _renderer;
    private NativeDigitalSpan[] _digitalSpans = [];
    private NativeAnalogSegment[] _analogSegments = [];
    private NativeDecoderAnnotation[] _decoderAnnotations = [];
    private int _digitalChannelCount = 1;
    private int _analogChannelCount;
    private int _decoderRowCount;
    private bool _isAttached;
    private bool _hasSessionSpans;
    private bool _hasLoggedNativeReady;
    private bool _isSettingViewport;

    public WaveformPanel()
    {
        _root = new Grid {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _swapChainPanel = new SwapChainPanel {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _textOverlay = new Canvas {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
        };

        _root.Children.Add(_swapChainPanel);
        _root.Children.Add(_textOverlay);
        Content = _root;
        Loaded += WaveformPanel_Loaded;
        Unloaded += WaveformPanel_Unloaded;
        SizeChanged += WaveformPanel_SizeChanged;
    }

    public SwapChainPanel SwapChainPanel => _swapChainPanel;

    public event EventHandler<WaveformViewportChangedEventArgs>? ViewportChanged;

    public float ViewportWidthPixels => Math.Max(1.0F, (float)(ActualWidth - PlotLeftMargin - PlotRightMargin));

    public double ViewportStartSeconds
    {
        get => (double)GetValue(ViewportStartSecondsProperty);
        set => SetValue(ViewportStartSecondsProperty, value);
    }

    public double SecondsPerPixel
    {
        get => (double)GetValue(SecondsPerPixelProperty);
        set => SetValue(SecondsPerPixelProperty, value);
    }

    private static void OnViewportPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is WaveformPanel panel) {
            if (panel._isSettingViewport) {
                return;
            }

            panel.TryUpdateRenderer();
        }
    }

    private void WaveformPanel_Loaded(object sender, RoutedEventArgs e)
    {
        TryUpdateRenderer();
    }

    private void WaveformPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TryUpdateRenderer();
    }

    private void WaveformPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _renderer?.Dispose();
        _renderer = null;
        _isAttached = false;
        _textOverlay.Children.Clear();
    }

    private void TryUpdateRenderer()
    {
        if (!IsLoaded || ActualWidth <= 0.0 || ActualHeight <= 0.0) {
            return;
        }

        try {
            _renderer ??= NativeWaveformRenderer.Create();
            if (!_isAttached) {
                _renderer.AttachSwapChainPanel(_swapChainPanel);
                _isAttached = true;
                WriteRenderingLog("Native renderer attached to SwapChainPanel.");
                var deviceInfo = _renderer.GetDeviceInfo();
                if (!string.IsNullOrWhiteSpace(deviceInfo)) {
                    WriteRenderingLog(deviceInfo);
                }
            }

            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var pixelWidth = Math.Max(1, (int)Math.Round(ActualWidth * scale));
            var pixelHeight = Math.Max(1, (int)Math.Round(ActualHeight * scale));
            var dpi = (float)(scale * 96.0);

            _renderer.Resize(pixelWidth, pixelHeight, dpi);
            ApplyDigitalSpans();
            ViewportChanged?.Invoke(
                this,
                new WaveformViewportChangedEventArgs(ViewportStartSeconds, SecondsPerPixel, ViewportWidthPixels));
        }
        catch (NativeException exception) {
            WriteRenderingLog($"Native renderer fallback: {exception.Message}");
            ShowFallback(exception.Message);
        }
    }

    public void SetDigitalSpans(IReadOnlyList<NativeDigitalSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        SetWaveformData(1, 0, 0, spans, [], []);
    }

    public void SetViewport(double startSeconds, double secondsPerPixel)
    {
        if (ViewportStartSeconds == startSeconds && SecondsPerPixel == secondsPerPixel) {
            return;
        }

        _isSettingViewport = true;
        try {
            ViewportStartSeconds = startSeconds;
            SecondsPerPixel = secondsPerPixel;
        }
        finally {
            _isSettingViewport = false;
        }
    }

    public void SetWaveformData(
        int digitalChannelCount,
        int analogChannelCount,
        IReadOnlyList<NativeDigitalSpan> digitalSpans,
        IReadOnlyList<NativeAnalogSegment> analogSegments)
    {
        SetWaveformData(digitalChannelCount, analogChannelCount, 0, digitalSpans, analogSegments, []);
    }

    public void SetWaveformData(
        int digitalChannelCount,
        int analogChannelCount,
        int decoderRowCount,
        IReadOnlyList<NativeDigitalSpan> digitalSpans,
        IReadOnlyList<NativeAnalogSegment> analogSegments,
        IReadOnlyList<NativeDecoderAnnotation> decoderAnnotations)
    {
        ArgumentNullException.ThrowIfNull(digitalSpans);
        ArgumentNullException.ThrowIfNull(analogSegments);
        ArgumentNullException.ThrowIfNull(decoderAnnotations);

        _digitalChannelCount = Math.Max(0, digitalChannelCount);
        _analogChannelCount = Math.Max(0, analogChannelCount);
        _decoderRowCount = Math.Max(0, decoderRowCount);
        _digitalSpans = digitalSpans as NativeDigitalSpan[] ?? digitalSpans.ToArray();
        _analogSegments = analogSegments as NativeAnalogSegment[] ?? analogSegments.ToArray();
        _decoderAnnotations = decoderAnnotations as NativeDecoderAnnotation[] ?? decoderAnnotations.ToArray();
        _hasSessionSpans = true;
        ApplyDigitalSpans();
    }

    public void ClearDigitalSpans()
    {
        _digitalSpans = [];
        _analogSegments = [];
        _decoderAnnotations = [];
        _digitalChannelCount = 1;
        _analogChannelCount = 0;
        _decoderRowCount = 0;
        _hasSessionSpans = false;
        ApplyDigitalSpans();
    }

    private void ApplyDigitalSpans()
    {
        if (_renderer is null || !_isAttached) {
            UpdateTextOverlay();
            return;
        }

        if (!_hasSessionSpans) {
            _renderer.SetViewport(ViewportStartSeconds, SecondsPerPixel);
            _renderer.ClearDigitalSpans();
        }
        else {
            _renderer.SetViewportWaveformData(
                ViewportStartSeconds,
                SecondsPerPixel,
                _digitalChannelCount,
                _analogChannelCount,
                _decoderRowCount,
                _digitalSpans,
                _analogSegments,
                _decoderAnnotations);
        }

        UpdateTextOverlay();
    }

    private void UpdateTextOverlay()
    {
        _textOverlay.Children.Clear();
        if (ActualWidth <= 0.0 || ActualHeight <= 0.0) {
            return;
        }

        var trackCount = TotalTrackCount();
        for (var track = 0; track < trackCount; track++) {
            AddTrackLabel(track, GetTrackLabel(track), TrackTop(track, trackCount), TrackBottom(track, trackCount));
        }

        if (!_hasSessionSpans || _decoderRowCount <= 0 || _decoderAnnotations.Length == 0) {
            return;
        }

        foreach (var annotation in _decoderAnnotations) {
            AddDecoderAnnotationText(annotation);
        }
    }

    private int TotalTrackCount()
    {
        if (!_hasSessionSpans) {
            return 1;
        }

        return Math.Max(1, _digitalChannelCount + _analogChannelCount + _decoderRowCount);
    }

    private double TrackTop(int trackIndex, int trackCount)
    {
        var availableHeight = Math.Max(1.0, ActualHeight - PlotTopMargin - PlotBottomMargin);
        return PlotTopMargin + availableHeight * trackIndex / trackCount;
    }

    private double TrackBottom(int trackIndex, int trackCount)
    {
        var availableHeight = Math.Max(1.0, ActualHeight - PlotTopMargin - PlotBottomMargin);
        return PlotTopMargin + availableHeight * (trackIndex + 1) / trackCount;
    }

    private string GetTrackLabel(int track)
    {
        if (!_hasSessionSpans) {
            return "D0";
        }

        if (track < _digitalChannelCount) {
            return $"D{track}";
        }

        if (track < _digitalChannelCount + _analogChannelCount) {
            return $"A{track - _digitalChannelCount}";
        }

        var decoderRow = track - _digitalChannelCount - _analogChannelCount;
        return decoderRow == 0 ? "UART" : $"DEC{decoderRow}";
    }

    private void AddTrackLabel(int track, string text, double top, double bottom)
    {
        var labelHost = new Border {
            Width = Math.Max(1.0, PlotLeftMargin - 14.0),
            Height = Math.Max(1.0, bottom - top),
            Child = new TextBlock {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Variable Text"),
                FontSize = track >= _digitalChannelCount + _analogChannelCount ? 13.0 : 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = _trackLabelBrush,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                MaxLines = 1,
            },
        };

        Canvas.SetLeft(labelHost, 14.0);
        Canvas.SetTop(labelHost, Math.Round(top));
        _textOverlay.Children.Add(labelHost);
    }

    private void AddDecoderAnnotationText(NativeDecoderAnnotation annotation)
    {
        if (annotation.RowIndex >= _decoderRowCount || string.IsNullOrWhiteSpace(annotation.Text)) {
            return;
        }

        var track = _digitalChannelCount + _analogChannelCount + annotation.RowIndex;
        var trackCount = TotalTrackCount();
        var top = TrackTop(track, trackCount) + DecoderTextInset;
        var bottom = TrackBottom(track, trackCount) - DecoderTextInset;
        var x0 = Math.Clamp(annotation.X0, 0.0F, ViewportWidthPixels);
        var x1 = Math.Clamp(annotation.X1, 0.0F, ViewportWidthPixels);
        var width = Math.Min(Math.Max(3.0, x1 - x0), Math.Max(0.0F, ViewportWidthPixels - x0));
        if (width < 14.0) {
            return;
        }

        var text = annotation.Text;
        var fontSize = Math.Clamp((width - 6.0) / Math.Max(1, text.Length) * 1.55, 9.0, 13.0);
        var host = new Border {
            Width = width,
            Height = Math.Max(18.0, bottom - top),
            Child = new TextBlock {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Variable Text"),
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = _decoderTextBrush,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxLines = 1,
            },
        };

        Canvas.SetLeft(host, Math.Round(PlotLeftMargin + x0));
        Canvas.SetTop(host, Math.Round(top));
        _textOverlay.Children.Add(host);
    }

    private void ShowFallback(string message)
    {
        _renderer?.Dispose();
        _renderer = null;
        _isAttached = false;
        _hasLoggedNativeReady = true;

        Content = new Border {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 65, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 203, 213, 225)),
                Text = message,
                TextWrapping = TextWrapping.WrapWholeWords,
            },
        };
    }

    private void WriteRenderingLog(string message)
    {
        if (_hasLoggedNativeReady && message.StartsWith("Native renderer attached", StringComparison.Ordinal)) {
            return;
        }

        try {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PulseView.WinUI",
                "logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "rendering.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            _hasLoggedNativeReady = true;
        }
        catch {
            // Rendering diagnostics must not affect the control lifecycle.
        }
    }
}

public sealed class WaveformViewportChangedEventArgs : EventArgs
{
    public WaveformViewportChangedEventArgs(double startSeconds, double secondsPerPixel, float widthPixels)
    {
        StartSeconds = startSeconds;
        SecondsPerPixel = secondsPerPixel;
        WidthPixels = widthPixels;
    }

    public double StartSeconds { get; }

    public double SecondsPerPixel { get; }

    public float WidthPixels { get; }
}

using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PulseView.App.Controls;
using PulseView.App.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PulseView.App;

public sealed partial class MainPage : Page
{
    private readonly ShellViewModel _viewModel = new();
    private readonly DispatcherTimer _acquisitionTimer = new() {
        Interval = TimeSpan.FromMilliseconds(50),
    };
    private nint _windowHandle;
    private bool _isDraggingWaveform;
    private uint _dragPointerId;
    private double _lastDragX;
    private bool _isWaveformUpdateQueued;
    private bool _isRefreshingControls;

    public MainPage()
    {
        InitializeComponent();

        InitializeCaptureControls();
        InitializeDecoderColorButtons();
        RootNavigationView.SelectedItem = MainNavigationItem;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        WaveformViewport.ViewportChanged += WaveformViewport_ViewportChanged;
        _acquisitionTimer.Tick += AcquisitionTimer_Tick;
        _acquisitionTimer.Start();
        Unloaded += MainPage_Unloaded;
        _viewModel.RefreshDevices();
        RefreshView();
    }

    public void InitializeWindowHandle(nint windowHandle)
    {
        _windowHandle = windowHandle;
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _windowHandle);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is null) {
            return;
        }

        try {
            await _viewModel.OpenFileAsync(file.Path);
        }
        catch {
            // The view model has already converted native errors into status text.
        }
    }

    private void AcquisitionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsAcquiring) {
            _viewModel.StopAcquisition();
        }
        else {
            _viewModel.StartAcquisition();
        }
    }

    private void ScanDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshDevices();
    }

    private void ConnectDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsSelectedDeviceLoaded) {
            _viewModel.DisconnectSelectedDevice();
        }
        else {
            _viewModel.ConnectSelectedDevice();
        }
    }

    private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls) {
            return;
        }

        _viewModel.SelectDevice(DeviceComboBox.SelectedItem as CaptureDeviceOption);
    }

    private void ChannelCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || ChannelCountComboBox.SelectedItem is not int channelCount) {
            return;
        }

        _viewModel.SetActiveDigitalChannelCount(channelCount);
    }

    private void SampleRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls) {
            return;
        }

        _viewModel.SetSampleRate(SampleRateComboBox.SelectedItem as CaptureSampleRateOption);
    }

    private void SampleCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls) {
            return;
        }

        _viewModel.SetSampleCount(SampleCountComboBox.SelectedItem as CaptureSampleCountOption);
    }

    private void TriggerEdgeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls) {
            return;
        }

        _viewModel.SetTriggerEdge(TriggerEdgeComboBox.SelectedItem as TriggerEdgeOption);
    }

    private void TriggerChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || TriggerChannelComboBox.SelectedIndex < 0) {
            return;
        }

        _viewModel.SetTriggerChannelIndex(TriggerChannelComboBox.SelectedIndex);
    }

    private void DecoderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls) {
            return;
        }

        _viewModel.SetDecoder(DecoderComboBox.SelectedItem as ProtocolDecoderOption);
    }

    private void DecoderChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DecoderChannelComboBox.SelectedIndex < 0) {
            return;
        }

        _viewModel.SetDecoderChannelIndex(DecoderChannelComboBox.SelectedIndex);
    }

    private void DecoderBaudComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DecoderBaudComboBox.SelectedItem is not int baudRate) {
            return;
        }

        _viewModel.SetDecoderBaudRate(baudRate);
    }

    private void DecoderDataBitsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DecoderDataBitsComboBox.SelectedItem is not int dataBits) {
            return;
        }

        _viewModel.SetDecoderDataBits(dataBits);
    }

    private void DecoderStopBitsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DecoderStopBitsComboBox.SelectedItem is not string stopBits) {
            return;
        }

        _viewModel.SetDecoderStopBits(stopBits);
    }

    private void DecoderParityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingControls || DecoderParityComboBox.SelectedItem is not string parity) {
            return;
        }

        _viewModel.SetDecoderParity(parity);
    }

    private void DecoderAddButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddConfiguredDecoder();
    }

    private void DecoderColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DecoderColorOption color }) {
            _viewModel.SetDecoderColor(color);
        }
    }

    private void DecoderRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int decoderId }) {
            _viewModel.EditConfiguredDecoder(decoderId);
        }
    }

    private void DecoderRowSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int decoderId }) {
            _viewModel.EditConfiguredDecoder(decoderId);
            DecoderSettingsButton.Flyout?.ShowAt(DecoderSettingsButton);
        }
    }

    private void DecoderRowDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int decoderId }) {
            _viewModel.RemoveConfiguredDecoder(decoderId);
        }
    }

    private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        UpdateSectionVisibility();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshView();
    }

    private void AcquisitionTimer_Tick(object? sender, object e)
    {
        if (!_viewModel.IsAcquiring) {
            return;
        }

        _viewModel.RefreshAcquisition();
    }

    private void WaveformViewport_ViewportChanged(object? sender, WaveformViewportChangedEventArgs e)
    {
        QueueWaveformUpdate();
    }

    private void PanLeftButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PanLeft();
    }

    private void PanRightButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PanRight();
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ZoomIn();
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ZoomOut();
    }

    private void ResetViewportButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetViewport(WaveformViewport.ViewportWidthPixels);
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _acquisitionTimer.Stop();
        _acquisitionTimer.Tick -= AcquisitionTimer_Tick;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        WaveformViewport.ViewportChanged -= WaveformViewport_ViewportChanged;
        _viewModel.Dispose();
    }

    private void RefreshView()
    {
        RefreshControlSelections();
        NativeBridgeVersionText.Text = _viewModel.NativeBridgeVersion;
        NativeRenderingVersionText.Text = _viewModel.NativeRenderingVersion;
        StatusText.Text = _viewModel.StatusMessage;
        DeviceStatusText.Text = _viewModel.DeviceStatusSummary;
        CaptureSettingsText.Text = _viewModel.CaptureSettingsSummary;
        DecoderStatusText.Text = _viewModel.DecodeSummary;
        CurrentFileText.Text = string.IsNullOrWhiteSpace(_viewModel.CurrentFilePath)
            ? "No device or capture loaded"
            : _viewModel.CurrentFilePath;
        SignalCountText.Text = _viewModel.SignalSummary;
        DurationText.Text = _viewModel.DurationSummary;
        AcquisitionText.Text = _viewModel.AcquisitionSummary;
        ViewportText.Text = _viewModel.ViewportSummary;
        WaveformViewport.SetViewport(_viewModel.ViewportStartSeconds, _viewModel.SecondsPerPixel);
        QueueWaveformUpdate();
        OpenFileButton.IsEnabled = !_viewModel.IsBusy;
        ScanDeviceButton.IsEnabled = !_viewModel.IsBusy;
        ConnectDeviceButton.IsEnabled = !_viewModel.IsBusy && _viewModel.CanLoadSelectedDevice;
        AcquisitionButton.IsEnabled = !_viewModel.IsBusy && _viewModel.CanAcquireSelectedDevice;
        UpdateAcquisitionButtonVisual();
        UpdateConnectDeviceButtonVisual();
        UpdateDecoderSettingsVisibility();
        UpdateDecoderColorSelection();
        RefreshDecoderRows();
        UpdateSectionVisibility();
    }

    private void UpdateConnectDeviceButtonVisual()
    {
        ConnectDeviceIdleIcon.Visibility = _viewModel.IsSelectedDeviceLoaded ? Visibility.Collapsed : Visibility.Visible;
        ConnectDeviceLoadedIcon.Visibility = _viewModel.IsSelectedDeviceLoaded ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(ConnectDeviceButton, _viewModel.IsSelectedDeviceLoaded ? "Disconnect device" : "Load selected device");
    }

    private void UpdateAcquisitionButtonVisual()
    {
        AcquisitionPauseIcon.Visibility = _viewModel.IsAcquiring ? Visibility.Collapsed : Visibility.Visible;
        AcquisitionPlayIcon.Visibility = _viewModel.IsAcquiring ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(AcquisitionButton, _viewModel.IsAcquiring ? "Stop acquisition" : "Start acquisition");
    }

    private void UpdateDecoderSettingsVisibility()
    {
        var isUartDecoder = _viewModel.SelectedDecoder.Kind == ProtocolDecoderKind.Uart;
        UartDecoderSettingsPanel.Visibility = isUartDecoder ? Visibility.Visible : Visibility.Collapsed;
        DecoderSettingsButton.IsEnabled = !_viewModel.IsBusy && isUartDecoder;
        DecoderAddButton.IsEnabled = !_viewModel.IsBusy && _viewModel.SelectedDecoder.Kind != ProtocolDecoderKind.None;
    }

    private void InitializeCaptureControls()
    {
        ChannelCountComboBox.ItemsSource = _viewModel.DigitalChannelCountOptions;
        SampleCountComboBox.ItemsSource = _viewModel.SampleCountOptions;
        TriggerEdgeComboBox.ItemsSource = _viewModel.TriggerEdgeOptions;
        DecoderComboBox.ItemsSource = _viewModel.DecoderOptions;
        DecoderBaudComboBox.ItemsSource = _viewModel.DecoderBaudRateOptions;
        DecoderDataBitsComboBox.ItemsSource = _viewModel.DecoderDataBitOptions;
        DecoderStopBitsComboBox.ItemsSource = _viewModel.DecoderStopBitOptions;
        DecoderParityComboBox.ItemsSource = _viewModel.DecoderParityOptions;
    }

    private void InitializeDecoderColorButtons()
    {
        DecoderColorGrid.Children.Clear();
        for (var index = 0; index < _viewModel.DecoderColorOptions.Count; index++) {
            var color = _viewModel.DecoderColorOptions[index];
            var button = new Button {
                Width = 28,
                Height = 24,
                MinWidth = 0,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(ToColor(color)),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(1),
                Tag = color,
            };
            ToolTipService.SetToolTip(button, color.Label);
            button.Click += DecoderColorButton_Click;
            Grid.SetRow(button, index / 4);
            Grid.SetColumn(button, index % 4);
            DecoderColorGrid.Children.Add(button);
        }
    }

    private void UpdateDecoderColorSelection()
    {
        foreach (var child in DecoderColorGrid.Children.OfType<Button>()) {
            var isSelected = child.Tag is DecoderColorOption color && color == _viewModel.DecoderColor;
            child.BorderBrush = new SolidColorBrush(isSelected ? Colors.White : Colors.Transparent);
            child.BorderThickness = new Thickness(isSelected ? 2 : 1);
        }
    }

    private void RefreshDecoderRows()
    {
        DecoderRowsPanel.Children.Clear();
        foreach (var decoder in _viewModel.ConfiguredDecoders) {
            var row = new Grid {
                ColumnSpacing = 8,
                Width = 340,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var decoderButton = new Button {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = decoder.Id,
                Content = CreateDecoderRowContent(decoder),
            };
            decoderButton.Click += DecoderRowButton_Click;
            Grid.SetColumn(decoderButton, 0);
            row.Children.Add(decoderButton);

            var settingsButton = CreateDecoderRowIconButton(Symbol.Setting, "Decoder settings", decoder.Id);
            settingsButton.Click += DecoderRowSettingsButton_Click;
            Grid.SetColumn(settingsButton, 1);
            row.Children.Add(settingsButton);

            var deleteButton = CreateDecoderRowIconButton(Symbol.Delete, "Delete decoder", decoder.Id);
            deleteButton.Click += DecoderRowDeleteButton_Click;
            Grid.SetColumn(deleteButton, 2);
            row.Children.Add(deleteButton);

            DecoderRowsPanel.Children.Add(row);
        }
    }

    private static Grid CreateDecoderRowContent(ConfiguredDecoder decoder)
    {
        var content = new Grid {
            ColumnSpacing = 8,
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new Border {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ToColor(decoder.Color)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var label = new TextBlock {
            Text = decoder.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        return content;
    }

    private static Button CreateDecoderRowIconButton(Symbol symbol, string tooltip, int decoderId)
    {
        var button = new Button {
            Width = 38,
            Height = 34,
            MinWidth = 0,
            Padding = new Thickness(0),
            Tag = decoderId,
            Content = new SymbolIcon(symbol),
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private void UpdateSectionVisibility()
    {
        if (MainSectionPanel is null || DecoderSectionPanel is null || ExpressionSectionPanel is null) {
            return;
        }

        var selectedTag = (RootNavigationView?.SelectedItem as NavigationViewItem)?.Tag as string ?? "Main";
        MainSectionPanel.Visibility = selectedTag == "Main" ? Visibility.Visible : Visibility.Collapsed;
        DecoderSectionPanel.Visibility = selectedTag == "Decoder" ? Visibility.Visible : Visibility.Collapsed;
        ExpressionSectionPanel.Visibility = selectedTag == "Expression" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Windows.UI.Color ToColor(DecoderColorOption color)
    {
        return Windows.UI.Color.FromArgb(0xFF, color.Red, color.Green, color.Blue);
    }

    private void RefreshControlSelections()
    {
        _isRefreshingControls = true;
        try {
            DeviceComboBox.ItemsSource = _viewModel.DeviceOptions;
            DeviceComboBox.SelectedItem = _viewModel.SelectedDevice;
            ChannelCountComboBox.SelectedItem = _viewModel.ActiveDigitalChannelCount;
            SampleRateComboBox.ItemsSource = _viewModel.AvailableSampleRates;
            SampleRateComboBox.SelectedItem = _viewModel.SelectedSampleRate;
            SampleCountComboBox.SelectedItem = _viewModel.SelectedSampleCount;
            TriggerEdgeComboBox.SelectedItem = _viewModel.SelectedTriggerEdge;
            TriggerChannelComboBox.ItemsSource = _viewModel.DecoderChannelOptions;
            TriggerChannelComboBox.SelectedIndex = _viewModel.TriggerChannelIndex;
            DecoderComboBox.SelectedItem = _viewModel.SelectedDecoder;
            DecoderChannelComboBox.ItemsSource = _viewModel.DecoderChannelOptions;
            DecoderChannelComboBox.SelectedIndex = _viewModel.DecoderChannelIndex;
            DecoderBaudComboBox.SelectedItem = _viewModel.DecoderBaudRate;
            DecoderDataBitsComboBox.SelectedItem = _viewModel.DecoderDataBits;
            DecoderStopBitsComboBox.SelectedItem = _viewModel.DecoderStopBits;
            DecoderParityComboBox.SelectedItem = _viewModel.DecoderParity;
        }
        finally {
            _isRefreshingControls = false;
        }
    }

    private void UpdateWaveformSpans(float widthPixels)
    {
        if (!_viewModel.HasSession) {
            WaveformViewport.ClearDigitalSpans();
            return;
        }

        WaveformViewport.SetWaveformData(
            _viewModel.DigitalChannelCount,
            _viewModel.AnalogChannelCount,
            _viewModel.DecoderRowCount,
            _viewModel.QueryDigitalSpans(widthPixels),
            _viewModel.QueryAnalogSegments(widthPixels),
            _viewModel.QueryDecoderAnnotations(widthPixels));
    }

    private void QueueWaveformUpdate()
    {
        if (_isWaveformUpdateQueued) {
            return;
        }

        _isWaveformUpdateQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isWaveformUpdateQueued = false;
            UpdateWaveformSpans(WaveformViewport.ViewportWidthPixels);
        });
    }

    private void WaveformViewport_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(WaveformViewport);
        var step = point.Properties.MouseWheelDelta > 0 ? -1 : 1;
        _viewModel.ZoomAtPixel(
            Math.Max(0.0, point.Position.X - WaveformPanel.PlotLeftMargin),
            step,
            WaveformViewport.ViewportWidthPixels);
        e.Handled = true;
    }

    private void WaveformViewport_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(WaveformViewport);
        if (!point.Properties.IsLeftButtonPressed) {
            return;
        }

        _isDraggingWaveform = true;
        _dragPointerId = point.PointerId;
        _lastDragX = point.Position.X;
        WaveformViewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void WaveformViewport_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingWaveform) {
            return;
        }

        var point = e.GetCurrentPoint(WaveformViewport);
        if (point.PointerId != _dragPointerId || !point.Properties.IsLeftButtonPressed) {
            EndWaveformDrag(e);
            return;
        }

        var deltaX = point.Position.X - _lastDragX;
        if (Math.Abs(deltaX) >= 0.1) {
            _viewModel.PanByPixels(-deltaX);
            _lastDragX = point.Position.X;
        }

        e.Handled = true;
    }

    private void WaveformViewport_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        EndWaveformDrag(e);
    }

    private void WaveformViewport_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        EndWaveformDrag(e);
    }

    private void WaveformViewport_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isDraggingWaveform = false;
    }

    private void EndWaveformDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingWaveform) {
            return;
        }

        _isDraggingWaveform = false;
        WaveformViewport.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}

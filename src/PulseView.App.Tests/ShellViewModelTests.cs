using PulseView.App.ViewModels;

namespace PulseView.App.Tests;

[TestClass]
public sealed class ShellViewModelTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ShellViewModelOpensCaptureFile()
    {
        var capturePath = Path.Combine(TestContext.TestRunDirectory!, "viewmodel-capture.sr");
        await File.WriteAllTextAsync(capturePath, "pulseview viewmodel test capture");

        using var viewModel = new ShellViewModel();

        await viewModel.OpenFileAsync(capturePath);

        Assert.AreEqual(capturePath, viewModel.CurrentFilePath);
        Assert.AreEqual(1, viewModel.SignalCount);
        Assert.IsTrue(viewModel.HasSession);
        Assert.IsGreaterThan(0.0, viewModel.DurationSeconds);
        Assert.IsTrue(viewModel.DurationSummary.StartsWith("Duration:", StringComparison.Ordinal));
        Assert.AreEqual("Capture opened", viewModel.StatusMessage);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsGreaterThan(0, viewModel.QueryDigitalSpans(640.0F).Length);
    }

    [TestMethod]
    public async Task ShellViewModelReportsOpenFailure()
    {
        using var viewModel = new ShellViewModel();

        try {
            await viewModel.OpenFileAsync(Path.Combine(TestContext.TestRunDirectory!, "missing.sr"));
            Assert.Fail("Expected open failure.");
        }
        catch {
            Assert.AreEqual("Capture file was not found.", viewModel.StatusMessage);
            Assert.IsFalse(viewModel.IsBusy);
        }
    }

    [TestMethod]
    public void ShellViewModelLoadsAndCapturesDemoDevice()
    {
        using var viewModel = new ShellViewModel();

        viewModel.LoadDemoDevice();

        Assert.IsTrue(viewModel.IsDemoDeviceLoaded);
        Assert.IsFalse(viewModel.IsAcquiring);
        Assert.AreEqual(10, viewModel.SignalCount);
        Assert.AreEqual(8, viewModel.DigitalChannelCount);
        Assert.AreEqual(2, viewModel.AnalogChannelCount);
        Assert.AreEqual("Demo device ready", viewModel.StatusMessage);

        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();

        Assert.IsTrue(viewModel.IsAcquiring);
        Assert.IsGreaterThan(0.0, viewModel.DurationSeconds);
        Assert.IsGreaterThan(0, viewModel.QueryDigitalSpans(640.0F).Length);
        Assert.IsGreaterThan(0, viewModel.QueryAnalogSegments(640.0F).Length);

        viewModel.StopDemoCapture();

        Assert.IsFalse(viewModel.IsAcquiring);
        Assert.AreEqual("Demo acquisition stopped", viewModel.StatusMessage);
    }

    [TestMethod]
    public void ShellViewModelAutoStopsDemoAtSelectedSampleCount()
    {
        using var viewModel = new ShellViewModel();

        viewModel.SetSampleCount(viewModel.SampleCountOptions.First(option => option.Samples == 100_000));
        viewModel.LoadDemoDevice();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();

        Assert.IsFalse(viewModel.IsAcquiring);
        Assert.AreEqual(100_000.0 / 10_000_000.0, viewModel.DurationSeconds, 1.0e-9);
        Assert.AreEqual("Demo acquisition complete", viewModel.StatusMessage);
    }

    [TestMethod]
    public void ShellViewModelAppliesCaptureChannelAndSampleRateSettings()
    {
        using var viewModel = new ShellViewModel();

        viewModel.LoadDemoDevice();
        viewModel.SetActiveDigitalChannelCount(4);

        Assert.AreEqual(4, viewModel.ActiveDigitalChannelCount);
        Assert.AreEqual(4, viewModel.DigitalChannelCount);
        Assert.AreEqual(6, viewModel.SignalCount);
        Assert.IsTrue(viewModel.AvailableSampleRates.Any(rate => rate.SamplesPerSecond == 40_000_000));
        Assert.IsFalse(viewModel.AvailableSampleRates.Any(rate => rate.SamplesPerSecond == 80_000_000));

        viewModel.SetActiveDigitalChannelCount(8);

        Assert.AreEqual(8, viewModel.ActiveDigitalChannelCount);
        Assert.IsFalse(viewModel.AvailableSampleRates.Any(rate => rate.SamplesPerSecond == 40_000_000));
        Assert.IsTrue(viewModel.CaptureSettingsSummary.Contains("8 digital channels", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ShellViewModelTracksTriggerSettings()
    {
        using var viewModel = new ShellViewModel();

        viewModel.SetTriggerEdge(viewModel.TriggerEdgeOptions.First(option => option.Kind == TriggerEdgeKind.Rising));
        viewModel.SetTriggerChannelIndex(7);

        Assert.AreEqual(TriggerEdgeKind.Rising, viewModel.SelectedTriggerEdge.Kind);
        Assert.AreEqual(7, viewModel.TriggerChannelIndex);
        Assert.AreEqual("Rising edge on D7", viewModel.TriggerSummary);

        viewModel.SetActiveDigitalChannelCount(4);

        Assert.AreEqual(3, viewModel.TriggerChannelIndex);
        Assert.IsTrue(viewModel.CaptureSettingsSummary.Contains("Rising edge on D3", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ShellViewModelScansSLogicCombo8Mode()
    {
        var alternateModeDevice = new CaptureDeviceOption(
            "daplink-test",
            "RV CMSIS-DAP",
            CaptureDeviceKind.SLogicCombo8OtherMode,
            "SLogic Combo 8",
            "Detected a debugger/serial mode device.",
            CanAcquire: false);
        var logicDevice = new CaptureDeviceOption(
            @"USB\VID_359F&PID_0300\SI_8CH",
            "USB TO LA",
            CaptureDeviceKind.SLogicCombo8Logic,
            "SLogic Combo 8",
            "Logic-analyzer mode detected.",
            CanAcquire: true,
            "{CDB3B5AD-293B-4663-AA36-1AAE46463776}");
        var duplicateLogicDevice = logicDevice with
        {
            Id = @"USB\VID_359F&PID_0300\SI_8CH#DUPLICATE",
            DeviceInterfaceGuid = null,
        };
        using var viewModel = new ShellViewModel(
            new SessionService(),
            new FakeHardwareDeviceProbe(alternateModeDevice, duplicateLogicDevice, logicDevice),
            new FakeHardwareTransportInspector(new CaptureDeviceConnection(true, "WinUSB open", [])));

        viewModel.RefreshDevices();
        viewModel.SelectDevice(logicDevice);
        viewModel.ConnectSelectedDevice();

        Assert.HasCount(2, viewModel.DeviceOptions);
        Assert.IsFalse(viewModel.DeviceOptions.Any(device => device.Kind == CaptureDeviceKind.SLogicCombo8OtherMode));
        Assert.AreEqual("SLogic Combo 8", viewModel.SelectedDevice.DisplayName);
        Assert.IsTrue(viewModel.CanAcquireSelectedDevice);
        Assert.IsTrue(viewModel.CanLoadSelectedDevice);
        Assert.IsTrue(viewModel.IsSelectedDeviceLoaded);
        Assert.AreEqual("Device status: ready", viewModel.DeviceStatusSummary);
        Assert.AreEqual("SLogic Combo 8", viewModel.CurrentFilePath);
        Assert.AreEqual(8, viewModel.SignalCount);
        Assert.AreEqual(8, viewModel.DigitalChannelCount);
    }

    [TestMethod]
    public void ShellViewModelConnectsSLogicWinUsbDiagnostics()
    {
        var device = new CaptureDeviceOption(
            @"USB\VID_359F&PID_0300\SI_8CH",
            "USB TO LA",
            CaptureDeviceKind.SLogicCombo8Logic,
            "SLogic Combo 8",
            "Logic-analyzer mode detected.",
            CanAcquire: true,
            "{CDB3B5AD-293B-4663-AA36-1AAE46463776}");
        var connection = new CaptureDeviceConnection(
            true,
            "WinUSB open: interface 0, alt 0, endpoints 2; 2 pipes",
            ["bulk 0x01, packet 512, interval 0", "bulk 0x81, packet 512, interval 0"]);
        using var viewModel = new ShellViewModel(
            new SessionService(),
            new FakeHardwareDeviceProbe(device),
            new FakeHardwareTransportInspector(connection));

        viewModel.RefreshDevices();
        viewModel.SelectDevice(device);
        viewModel.ConnectSelectedDevice();

        Assert.AreEqual("SLogic Combo 8", viewModel.CurrentFilePath);
        Assert.AreEqual(connection.Summary, viewModel.DeviceTransportSummary);
        Assert.HasCount(2, viewModel.DeviceConnectionDetails);
        Assert.IsTrue(viewModel.CanAcquireSelectedDevice);
        Assert.IsTrue(viewModel.IsSelectedDeviceLoaded);

        viewModel.StartAcquisition();
        Thread.Sleep(10);
        viewModel.RefreshAcquisition();

        Assert.IsTrue(viewModel.IsAcquiring);
        Assert.IsGreaterThan(0.0, viewModel.DurationSeconds);
        var hardwareSpans = viewModel.QueryDigitalSpans(640.0F);
        Assert.HasCount(8, hardwareSpans);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 8).Select(channel => (byte)channel).ToArray(),
            hardwareSpans.Select(span => span.ChannelIndex).ToArray());
        Assert.IsTrue(hardwareSpans.All(span => span.X0 == 0.0F && span.X1 > span.X0 && span.Level == 0 && span.EdgeFlags == 0));

        viewModel.StopAcquisition();

        Assert.IsFalse(viewModel.IsAcquiring);
        Assert.AreEqual("SLogic Combo 8 acquisition stopped", viewModel.StatusMessage);

        viewModel.DisconnectSelectedDevice();

        Assert.IsFalse(viewModel.IsSelectedDeviceLoaded);
        Assert.IsTrue(viewModel.CanLoadSelectedDevice);
        Assert.AreEqual("Device disconnected", viewModel.StatusMessage);
        Assert.AreEqual(0, viewModel.SignalCount);
        Assert.AreEqual("Device status: ready", viewModel.DeviceStatusSummary);
    }

    [TestMethod]
    public void ShellViewModelStopsSLogicAcquisitionAtSelectedSampleCount()
    {
        var device = new CaptureDeviceOption(
            @"USB\VID_359F&PID_0300\SI_8CH",
            "USB TO LA",
            CaptureDeviceKind.SLogicCombo8Logic,
            "SLogic Combo 8",
            "Logic-analyzer mode detected.",
            CanAcquire: true,
            "{CDB3B5AD-293B-4663-AA36-1AAE46463776}");
        using var viewModel = new ShellViewModel(
            new SessionService(),
            new FakeHardwareDeviceProbe(device),
            new FakeHardwareTransportInspector(new CaptureDeviceConnection(true, "WinUSB open", [])));

        viewModel.SetSampleCount(viewModel.SampleCountOptions.First(option => option.Samples == 100_000));
        viewModel.RefreshDevices();
        viewModel.SelectDevice(device);
        viewModel.ConnectSelectedDevice();
        viewModel.StartAcquisition();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();

        Assert.IsFalse(viewModel.IsAcquiring);
        Assert.AreEqual(100_000.0 / 10_000_000.0, viewModel.DurationSeconds, 1.0e-9);
        Assert.AreEqual("SLogic Combo 8 acquisition complete", viewModel.StatusMessage);
        Assert.HasCount(8, viewModel.QueryDigitalSpans(640.0F));
    }

    [TestMethod]
    public void ShellViewModelShowsSLogicSamplePointsWhenSamplesAreSeparated()
    {
        var device = new CaptureDeviceOption(
            @"USB\VID_359F&PID_0300\SI_8CH",
            "USB TO LA",
            CaptureDeviceKind.SLogicCombo8Logic,
            "SLogic Combo 8",
            "Logic-analyzer mode detected.",
            CanAcquire: true,
            "{CDB3B5AD-293B-4663-AA36-1AAE46463776}");
        using var viewModel = new ShellViewModel(
            new SessionService(),
            new FakeHardwareDeviceProbe(device),
            new FakeHardwareTransportInspector(new CaptureDeviceConnection(true, "WinUSB open", [])));

        viewModel.RefreshDevices();
        viewModel.SelectDevice(device);
        viewModel.ConnectSelectedDevice();
        viewModel.StartAcquisition();
        Thread.Sleep(10);
        viewModel.RefreshAcquisition();
        viewModel.StopAcquisition();

        Assert.IsFalse(viewModel.QueryDigitalSpans(640.0F).Any(span => (span.EdgeFlags & 8) != 0));

        for (var step = 0; step < 16; step++) {
            viewModel.ZoomIn();
        }

        Assert.IsTrue(viewModel.QueryDigitalSpans(640.0F).Any(span => (span.EdgeFlags & 8) != 0));
    }

    [TestMethod]
    public void ShellViewModelMarksFailedHardwareConnectionUnknown()
    {
        var device = new CaptureDeviceOption(
            @"USB\VID_359F&PID_0300\SI_8CH",
            "USB TO LA",
            CaptureDeviceKind.SLogicCombo8Logic,
            "SLogic Combo 8",
            "Logic-analyzer mode detected.",
            CanAcquire: true,
            "{CDB3B5AD-293B-4663-AA36-1AAE46463776}");
        using var viewModel = new ShellViewModel(
            new SessionService(),
            new FakeHardwareDeviceProbe(device),
            new FakeHardwareTransportInspector(new CaptureDeviceConnection(false, "No WinUSB interface", [])));

        viewModel.RefreshDevices();
        viewModel.SelectDevice(device);
        viewModel.ConnectSelectedDevice();

        Assert.IsFalse(viewModel.IsSelectedDeviceLoaded);
        Assert.IsFalse(viewModel.CanLoadSelectedDevice);
        Assert.AreEqual("Device status: unknown", viewModel.DeviceStatusSummary);
    }

    [TestMethod]
    public void ShellViewModelMarksMissingSLogicDisconnectedDuringScan()
    {
        var device = new CaptureDeviceOption(
            @"USB\VID_359F&PID_0300\SI_8CH",
            "USB TO LA",
            CaptureDeviceKind.SLogicCombo8Logic,
            "SLogic Combo 8",
            "Logic-analyzer mode detected.",
            CanAcquire: true,
            "{CDB3B5AD-293B-4663-AA36-1AAE46463776}");
        using var viewModel = new ShellViewModel(
            new SessionService(),
            new FakeHardwareDeviceProbe(device),
            new FakeHardwareTransportInspector(new CaptureDeviceConnection(false, "No WinUSB device interface path was found for SLogic Combo 8.", [])));

        viewModel.RefreshDevices();
        viewModel.SelectDevice(viewModel.DeviceOptions.Single(option => option.Kind == CaptureDeviceKind.SLogicCombo8Logic));

        Assert.IsFalse(viewModel.IsSelectedDeviceLoaded);
        Assert.IsFalse(viewModel.CanLoadSelectedDevice);
        Assert.AreEqual("Device status: disconnected", viewModel.DeviceStatusSummary);
    }

    [TestMethod]
    public void ShellViewModelDecodesDemoUartAnnotations()
    {
        using var viewModel = new ShellViewModel();

        viewModel.LoadDemoDevice();
        viewModel.AddConfiguredDecoder();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        Assert.IsGreaterThan(0, viewModel.DecodeAnnotations.Count);
        Assert.AreEqual(1, viewModel.DecoderRowCount);
        Assert.IsGreaterThan(0, viewModel.QueryDecoderAnnotations(640.0F).Length);
        Assert.AreEqual("UART", viewModel.DecodeAnnotations[0].Decoder);
        Assert.AreEqual("H", viewModel.DecodeAnnotations[0].Text);
        Assert.AreEqual("UART", viewModel.SelectedDecoder.Label);
        Assert.AreEqual(8, viewModel.DecoderDataBits);
        Assert.AreEqual("1", viewModel.DecoderStopBits);
        Assert.AreEqual("None", viewModel.DecoderParity);
        Assert.AreEqual("8N1", viewModel.ConfiguredDecoders[0].FrameFormat);
        Assert.IsTrue(viewModel.DecodeSummary.Contains("annotations", StringComparison.Ordinal));

        viewModel.EditConfiguredDecoder(viewModel.ConfiguredDecoders[0].Id);
        viewModel.SetDecoderParity("Even");

        Assert.IsEmpty(viewModel.DecodeAnnotations);
    }

    [TestMethod]
    public void ShellViewModelAddsEditsAndRemovesDecoderRows()
    {
        using var viewModel = new ShellViewModel();

        var blue = viewModel.DecoderColorOptions.First(color => color.Label == "Blue");
        viewModel.SetDecoderColor(blue);
        viewModel.AddConfiguredDecoder();

        Assert.HasCount(1, viewModel.ConfiguredDecoders);
        Assert.AreEqual(1, viewModel.DecoderRowCount);
        Assert.AreEqual(blue, viewModel.ConfiguredDecoders[0].Color);

        viewModel.LoadDemoDevice();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        var nativeAnnotations = viewModel.QueryDecoderAnnotations(640.0F);
        Assert.IsGreaterThan(0, nativeAnnotations.Length);
        Assert.IsTrue(nativeAnnotations.All(annotation => annotation.RowIndex == 0));
        Assert.IsTrue(nativeAnnotations.All(annotation => annotation.ColorRed == blue.Red));

        viewModel.EditConfiguredDecoder(viewModel.ConfiguredDecoders[0].Id);
        viewModel.SetDecoderChannelIndex(2);

        Assert.AreEqual(2, viewModel.ConfiguredDecoders[0].ChannelIndex);

        viewModel.RemoveConfiguredDecoder(viewModel.ConfiguredDecoders[0].Id);

        Assert.IsEmpty(viewModel.ConfiguredDecoders);
        Assert.AreEqual(0, viewModel.DecoderRowCount);
        Assert.IsEmpty(viewModel.QueryDecoderAnnotations(640.0F));
    }

    [TestMethod]
    public void ShellViewModelQueriesDemoDecoderAnnotationsForCurrentViewport()
    {
        using var viewModel = new ShellViewModel();

        viewModel.LoadDemoDevice();
        viewModel.AddConfiguredDecoder();
        viewModel.StartDemoCapture();
        Thread.Sleep(45);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        viewModel.PanByPixels(2000.0);

        var annotations = viewModel.QueryDecoderAnnotations(640.0F);

        Assert.IsGreaterThan(0, annotations.Length);
        Assert.IsTrue(annotations.Any(annotation => annotation.X0 >= 0.0F && annotation.X0 <= 640.0F));
    }

    [TestMethod]
    public void ShellViewModelUsesDecoderOverviewWhenAnnotationsAreTooDense()
    {
        using var viewModel = new ShellViewModel();
        const float widthPixels = 640.0F;

        viewModel.LoadDemoDevice();
        viewModel.AddConfiguredDecoder();
        viewModel.StartDemoCapture();
        Thread.Sleep(180);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();
        viewModel.ResetViewport(widthPixels);

        var annotations = viewModel.QueryDecoderAnnotations(widthPixels);

        Assert.HasCount(1, annotations);
        Assert.AreEqual(string.Empty, annotations[0].Text);
        Assert.IsLessThanOrEqualTo(1.0F, annotations[0].X0);
        Assert.IsGreaterThan(widthPixels - 4.0F, annotations[0].X1);
    }

    [TestMethod]
    public void ShellViewModelClipsDecoderAnnotationsAtCaptureEnd()
    {
        using var viewModel = new ShellViewModel();
        const float widthPixels = 640.0F;

        viewModel.LoadDemoDevice();
        viewModel.AddConfiguredDecoder();
        viewModel.StartDemoCapture();
        Thread.Sleep(30);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        var captureWidthPixels = viewModel.DurationSeconds / viewModel.SecondsPerPixel;
        viewModel.PanByPixels(Math.Max(0.0, captureWidthPixels - widthPixels * 0.75));
        var captureEndPixel = (viewModel.DurationSeconds - viewModel.ViewportStartSeconds) / viewModel.SecondsPerPixel;
        var annotations = viewModel.QueryDecoderAnnotations(widthPixels);

        Assert.IsGreaterThan(0, annotations.Length);
        Assert.IsTrue(annotations.All(annotation => annotation.X1 <= captureEndPixel + 1.0e-3));
    }

    [TestMethod]
    public void ShellViewModelUsesFineAnalogSegmentsWhenNotInOverview()
    {
        using var viewModel = new ShellViewModel();

        viewModel.SetSampleRate(viewModel.AvailableSampleRates.First(rate => rate.SamplesPerSecond == 1_000_000));
        viewModel.LoadDemoDevice();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        var segments = viewModel.QueryAnalogSegments(240.0F);
        var lineSegments = segments.Where(segment => (segment.Flags & 1) == 0).ToArray();

        Assert.IsGreaterThan(0, lineSegments.Length);
        Assert.IsTrue(lineSegments.Take(64).All(segment => segment.X1 - segment.X0 <= 0.5001F));
    }

    [TestMethod]
    public void ShellViewModelShowsAnalogSamplePointsOnlyWhenSamplesAreSeparated()
    {
        using var viewModel = new ShellViewModel();

        viewModel.SetSampleRate(viewModel.AvailableSampleRates.First(rate => rate.SamplesPerSecond == 1_000_000));
        viewModel.LoadDemoDevice();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        Assert.IsFalse(viewModel.QueryAnalogSegments(240.0F).Any(segment => (segment.Flags & 2) != 0));

        for (var step = 0; step < 6; step++) {
            viewModel.ZoomIn();
        }

        Assert.IsTrue(viewModel.QueryAnalogSegments(240.0F).Any(segment => (segment.Flags & 2) != 0));
    }

    [TestMethod]
    public void ShellViewModelShowsDigitalSamplePointsOnlyWhenSamplesAreSeparated()
    {
        using var viewModel = new ShellViewModel();

        viewModel.SetSampleRate(viewModel.AvailableSampleRates.First(rate => rate.SamplesPerSecond == 1_000_000));
        viewModel.LoadDemoDevice();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        Assert.IsFalse(viewModel.QueryDigitalSpans(240.0F).Any(span => (span.EdgeFlags & 8) != 0));

        for (var step = 0; step < 6; step++) {
            viewModel.ZoomIn();
        }

        Assert.IsTrue(viewModel.QueryDigitalSpans(240.0F).Any(span => (span.EdgeFlags & 8) != 0));
    }

    [TestMethod]
    public void ShellViewModelKeepsDigitalLevelStableNearViewportStartEdge()
    {
        using var viewModel = new ShellViewModel();

        viewModel.LoadDemoDevice();
        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        const int channel = 5;
        var halfPeriod = 80.0e-6 * Math.Pow(1.55, channel);
        var phase = channel * 17.0e-6;
        var fallingEdgeSeconds = 2.0 * halfPeriod - phase;
        var startSeconds = fallingEdgeSeconds - halfPeriod * 0.005;
        viewModel.PanByPixels(startSeconds / viewModel.SecondsPerPixel);

        var firstVisibleSpan = viewModel.QueryDigitalSpans(640.0F)
            .Where(span => span.ChannelIndex == channel && (span.EdgeFlags & 8) == 0 && (span.EdgeFlags & 4) == 0)
            .OrderBy(span => span.X0)
            .First();

        Assert.AreEqual(0.0F, firstVisibleSpan.X0, 1.0e-6F);
        Assert.AreEqual(1, firstVisibleSpan.Level);
        Assert.AreEqual(0, firstVisibleSpan.EdgeFlags);
    }

    [TestMethod]
    public void ShellViewModelUpdatesViewport()
    {
        using var viewModel = new ShellViewModel();
        var initialSecondsPerPixel = viewModel.SecondsPerPixel;

        viewModel.ZoomIn();
        Assert.IsLessThan(initialSecondsPerPixel, viewModel.SecondsPerPixel);

        viewModel.PanRight();
        Assert.IsGreaterThan(0.0, viewModel.ViewportStartSeconds);

        viewModel.ResetViewport();
        Assert.AreEqual(0.0, viewModel.ViewportStartSeconds);
        Assert.AreEqual(initialSecondsPerPixel, viewModel.SecondsPerPixel);
    }

    [TestMethod]
    public void ShellViewModelZoomsAroundPointer()
    {
        using var viewModel = new ShellViewModel();

        viewModel.PanByPixels(100.0);
        var pivotBefore = viewModel.ViewportStartSeconds + 200.0 * viewModel.SecondsPerPixel;

        viewModel.ZoomAtPixel(200.0, -1);

        var pivotAfter = viewModel.ViewportStartSeconds + 200.0 * viewModel.SecondsPerPixel;
        Assert.AreEqual(pivotBefore, pivotAfter, 1.0e-12);
    }

    [TestMethod]
    public void ShellViewModelZoomInRightBlankPullsCaptureEndTowardPointer()
    {
        using var viewModel = new ShellViewModel();
        const double widthPixels = 1000.0;

        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();
        viewModel.ResetViewport((float)widthPixels);
        for (var step = 0; step < 8; step++) {
            viewModel.ZoomOut();
            if (VisibleCaptureEndPixel(viewModel) < widthPixels * 0.75) {
                break;
            }
        }

        var pivotPixel = widthPixels * 0.9;
        var previousEndPixel = VisibleCaptureEndPixel(viewModel);
        Assert.IsLessThan(pivotPixel, previousEndPixel);

        for (var step = 0; step < 8 && VisibleCaptureEndPixel(viewModel) < pivotPixel - 1.0e-6; step++) {
            previousEndPixel = VisibleCaptureEndPixel(viewModel);
            viewModel.ZoomAtPixel(pivotPixel, -1, widthPixels);
            var currentEndPixel = VisibleCaptureEndPixel(viewModel);
            Assert.IsGreaterThan(previousEndPixel, currentEndPixel);
            Assert.IsLessThanOrEqualTo(pivotPixel + 1.0e-6, currentEndPixel);
        }

        Assert.AreEqual(pivotPixel, VisibleCaptureEndPixel(viewModel), 1.0e-6);
        var pivotBefore = viewModel.ViewportStartSeconds + pivotPixel * viewModel.SecondsPerPixel;

        viewModel.ZoomAtPixel(pivotPixel, -1, widthPixels);

        var pivotAfter = viewModel.ViewportStartSeconds + pivotPixel * viewModel.SecondsPerPixel;
        Assert.AreEqual(pivotBefore, pivotAfter, 1.0e-9);
    }

    [TestMethod]
    public void ShellViewModelUsesOverviewPrimitivesWhenZoomedOut()
    {
        using var viewModel = new ShellViewModel();
        const float widthPixels = 640.0F;

        viewModel.StartDemoCapture();
        Thread.Sleep(160);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();
        for (var step = 0; step < 5; step++) {
            viewModel.ZoomOut();
        }

        var digitalSpans = viewModel.QueryDigitalSpans(widthPixels);
        var analogSegments = viewModel.QueryAnalogSegments(widthPixels);

        Assert.IsTrue(digitalSpans.Any(span => (span.EdgeFlags & 4) != 0));
        Assert.IsTrue(analogSegments.Any(segment => (segment.Flags & 1) != 0));
        Assert.IsLessThanOrEqualTo((int)(widthPixels * 4.0F), digitalSpans.Length);
        Assert.IsLessThanOrEqualTo((int)(2.0F * widthPixels), analogSegments.Length);
    }

    [TestMethod]
    public void ShellViewModelResetFitsCaptureDuration()
    {
        using var viewModel = new ShellViewModel();

        viewModel.StartDemoCapture();
        Thread.Sleep(25);
        viewModel.RefreshAcquisition();
        viewModel.StopDemoCapture();

        var duration = viewModel.DurationSeconds;
        viewModel.ResetViewport(1000.0F);

        Assert.AreEqual(0.0, viewModel.ViewportStartSeconds);
        Assert.AreEqual(duration / 1000.0, viewModel.SecondsPerPixel, 1.0e-9);
    }

    private static double VisibleCaptureEndPixel(ShellViewModel viewModel)
    {
        return (viewModel.DurationSeconds - viewModel.ViewportStartSeconds) / viewModel.SecondsPerPixel;
    }

    private sealed class FakeHardwareDeviceProbe(params CaptureDeviceOption[] devices) : IHardwareDeviceProbe
    {
        public IReadOnlyList<CaptureDeviceOption> ScanDevices()
        {
            return devices;
        }
    }

    private sealed class FakeHardwareTransportInspector(CaptureDeviceConnection connection) : IHardwareTransportInspector
    {
        public CaptureDeviceConnection Inspect(CaptureDeviceOption device)
        {
            return connection;
        }
    }
}

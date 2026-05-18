using PulseView.App.NativeInterop;

namespace PulseView.App.Tests;

[TestClass]
public sealed class NativeInteropTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NativeVersionCanBeRead()
    {
        Assert.AreEqual("PulseView.NativeBridge 0.1.0", NativeLibraryInfo.GetVersion());
    }

    [TestMethod]
    public void NativeRenderingVersionCanBeRead()
    {
        Assert.AreEqual("PulseView.Rendering.Native 0.1.0", NativeRenderingInfo.GetVersion());
    }

    [TestMethod]
    public void NativeRendererUsesSafeHandle()
    {
        using var renderer = NativeWaveformRenderer.Create();

        renderer.SetViewport(0.001, 0.000001);
        renderer.SetChannelCounts(2, 1);
        renderer.SetDigitalSpans([
            new NativeDigitalSpan(0.0F, 120.0F, 1, 1, 0),
            new NativeDigitalSpan(120.0F, 240.0F, 0, 2, 1),
        ]);
        renderer.SetAnalogSegments([
            new NativeAnalogSegment(0.0F, -0.5F, 120.0F, 0.5F, 0),
            new NativeAnalogSegment(120.0F, 0.5F, 240.0F, -0.25F, 0),
        ]);
        renderer.SetWaveformData(
            2,
            1,
            [
                new NativeDigitalSpan(0.0F, 120.0F, 1, 1, 0),
                new NativeDigitalSpan(120.0F, 240.0F, 0, 2, 1),
            ],
            [
                new NativeAnalogSegment(0.0F, -0.5F, 120.0F, 0.5F, 0),
                new NativeAnalogSegment(120.0F, 0.5F, 240.0F, -0.25F, 0),
            ]);
        renderer.SetViewportWaveformData(
            0.0,
            0.000001,
            2,
            1,
            1,
            [
                new NativeDigitalSpan(0.0F, 120.0F, 1, 1, 0),
                new NativeDigitalSpan(120.0F, 240.0F, 0, 2, 1),
            ],
            [
                new NativeAnalogSegment(0.0F, -0.5F, 120.0F, 0.5F, 0),
                new NativeAnalogSegment(120.0F, 0.5F, 240.0F, -0.25F, 0),
            ],
            [
                new NativeDecoderAnnotation(16.0F, 80.0F, 0, "0x55"),
            ]);
        Assert.IsFalse(string.IsNullOrWhiteSpace(renderer.GetDeviceInfo()));
        renderer.SetDigitalSpans([]);
        renderer.ClearDigitalSpans();
        renderer.RenderDemo();
    }

    [TestMethod]
    public void NativeSessionUsesSafeHandle()
    {
        using var session = NativeSession.Create();

        Assert.AreEqual(0, session.SignalCount);
        Assert.AreEqual(0.0, session.DurationSeconds);
    }

    [TestMethod]
    public void NativeSessionCanOpenExistingFile()
    {
        var capturePath = Path.Combine(TestContext.TestRunDirectory!, "sample-capture.sr");
        File.WriteAllText(capturePath, "pulseview test capture");

        using var session = NativeSession.Create();

        session.OpenFile(capturePath);
        Assert.AreEqual(1, session.SignalCount);
        Assert.IsGreaterThan(0.0, session.DurationSeconds);
    }

    [TestMethod]
    public void NativeSessionCanQueryDigitalSpans()
    {
        var capturePath = Path.Combine(TestContext.TestRunDirectory!, "span-capture.sr");
        File.WriteAllText(capturePath, "pulseview digital span test capture");

        using var session = NativeSession.Create();
        session.OpenFile(capturePath);

        var spans = session.QueryDigitalSpans(0.0, 10.0e-6, 640.0F);

        Assert.IsGreaterThan(0, spans.Length);
        Assert.IsGreaterThanOrEqualTo(0.0F, spans[0].X0);
        Assert.IsLessThanOrEqualTo(640.0F, spans[^1].X1);
    }

    [TestMethod]
    public void NativeSessionReportsMissingFile()
    {
        using var session = NativeSession.Create();

        try {
            session.OpenFile(Path.Combine(TestContext.TestRunDirectory!, "missing.sr"));
            Assert.Fail("Expected NativeException.");
        }
        catch (NativeException exception) {
            Assert.AreEqual("Capture file was not found.", exception.Message);
        }
    }
}

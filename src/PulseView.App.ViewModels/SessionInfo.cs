namespace PulseView.App.ViewModels;

public sealed record SessionInfo(string FilePath, int SignalCount, double DurationSeconds);

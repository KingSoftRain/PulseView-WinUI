namespace PulseView.App.ViewModels;

internal static class ViewportScaleLadder
{
    private static readonly double[] Levels = CreateLevels();

    public static double Step(double currentSecondsPerPixel, int stepCount)
    {
        if (!double.IsFinite(currentSecondsPerPixel) || currentSecondsPerPixel <= 0.0 || stepCount == 0) {
            return currentSecondsPerPixel;
        }

        var index = FindNearestIndex(currentSecondsPerPixel);
        var nextIndex = Math.Clamp(index + stepCount, 0, Levels.Length - 1);
        return Levels[nextIndex];
    }

    private static int FindNearestIndex(double value)
    {
        var index = Array.BinarySearch(Levels, value);
        if (index >= 0) {
            return index;
        }

        var upper = ~index;
        if (upper <= 0) {
            return 0;
        }

        if (upper >= Levels.Length) {
            return Levels.Length - 1;
        }

        var lower = upper - 1;
        return value / Levels[lower] <= Levels[upper] / value ? lower : upper;
    }

    private static double[] CreateLevels()
    {
        var levels = new List<double>();
        for (var exponent = -9; exponent <= 1; exponent++) {
            var scale = Math.Pow(10.0, exponent);
            levels.Add(1.0 * scale);
            levels.Add(2.0 * scale);
            levels.Add(5.0 * scale);
        }

        return levels.Distinct().Order().ToArray();
    }
}

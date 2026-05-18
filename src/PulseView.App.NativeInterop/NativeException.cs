namespace PulseView.App.NativeInterop;

public sealed class NativeException : Exception
{
    public NativeException(string message)
        : base(message)
    {
    }
}

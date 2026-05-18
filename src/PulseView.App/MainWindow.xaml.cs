using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace PulseView.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        RootFrame.Navigate(typeof(MainPage));
        if (RootFrame.Content is MainPage mainPage) {
            mainPage.InitializeWindowHandle(WindowNative.GetWindowHandle(this));
        }
    }
}

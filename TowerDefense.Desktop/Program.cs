using Avalonia;
using System;

namespace TowerDefense.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new AvaloniaNativePlatformOptions()
            {
                RenderingMode = [AvaloniaNativeRenderingMode.OpenGl]
            });
}

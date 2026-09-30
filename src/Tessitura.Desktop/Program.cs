using Avalonia;
using Tessitura.App;

namespace Tessitura.Desktop;

internal static class Program
{
    private static void Main(string[] args)
    {
        AppBuilder.Configure<TessituraApplication>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}

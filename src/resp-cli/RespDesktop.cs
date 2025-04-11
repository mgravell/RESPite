using StackExchange.Redis.Gui;
using Terminal.Gui;

namespace StackExchange.Redis;

internal class RespDesktop
{
    public static void Run(ConnectionOptionsBag options)
    {
        Application.Init();

        try
        {
            using var window = new RespDesktopWindow(options);
            Application.Run(window);
        }
        finally
        {
            Application.Shutdown();
        }
    }
}

namespace BengaliKeyboard.App;

/// <summary>
/// Application entry point enforcing single-instance execution before initializing the WPF runtime.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstance = new SingleInstanceManager();
        if (!singleInstance.IsFirstInstance)
        {
            // Signal the running instance to show its window and exit immediately
            _ = singleInstance.SignalFirstInstanceAsync().GetAwaiter().GetResult();
            return 0;
        }

        using var bootstrapper = new Bootstrapper();
        var app = new App(bootstrapper, singleInstance);
        return app.Run();
    }
}

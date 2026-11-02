using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
namespace FossPDF.Previewer;

internal static class Program
{
    // Windows OLE clipboard operations require the UI thread to be STA. Keep this a
    // synchronous entry point: the generated wrapper for async Main does not carry STAThread.
    [STAThread]
    public static void Main(string[] args)
    {
        var applicationPort = args.Length > 0 && int.TryParse(args[0], out var port) ? port : 12500;
        var communicationTask = CommunicationService.Instance.Start(applicationPort);

        try
        {
            if (Application.Current?.ApplicationLifetime is ClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new PreviewerWindow
                {
                    DataContext = new PreviewerWindowViewModel()
                };
                desktop.MainWindow.Show();
                desktop.Start(Array.Empty<string>());
                return;
            }

            AppBuilder
                .Configure(() => new PreviewerApp())
                .UsePlatformDetect()
                .UseReactiveUI()
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        finally
        {
            // The Avalonia loop has stopped; host cleanup must not resume on its dispatcher.
            SynchronizationContext.SetSynchronizationContext(null);
            CommunicationService.Instance.Stop().GetAwaiter().GetResult();
            communicationTask.GetAwaiter().GetResult();
        }
    }
}

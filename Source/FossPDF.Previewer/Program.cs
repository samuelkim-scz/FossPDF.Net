using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using FossPDF.Previewer;

var applicationPort = GetCommunicationPort();
var communicationTask = CommunicationService.Instance.Start(applicationPort);

try
{
    if(Application.Current?.ApplicationLifetime is ClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = new PreviewerWindow()
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
    // The Avalonia dispatcher has ended. Start teardown outside its synchronization context,
    // including synchronous portions of host disposal that could otherwise capture that context.
    await Task.Run(async () =>
    {
        try { await communicationTask.ConfigureAwait(false); }
        finally { await CommunicationService.Instance.Stop().ConfigureAwait(false); }
    }).ConfigureAwait(false);
}

static int GetCommunicationPort()
{
    const int defaultApplicationPort = 12500;
    
    var arguments = Environment.GetCommandLineArgs();

    if (arguments.Length < 2)
        return defaultApplicationPort;

    return int.TryParse(arguments[1], out var port) ? port : defaultApplicationPort;
}    

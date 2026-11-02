using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace FossPDF.Previewer;

internal partial class PreviewerWindow : Window
{
    public PreviewerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // ListBox consumes navigation keys before bubbling KeyDown handlers run.
        this.FindControl<ListBox>("ElementHierarchy")!.AddHandler(
            KeyDownEvent, HierarchyKeyDown, RoutingStrategies.Tunnel);
    }
    private InspectionViewModel? Inspector => (DataContext as PreviewerWindowViewModel)?.Inspector;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Numeric picking leaves focus on a form control rather than on the canvas.
        if (!e.Handled && e.Key == Key.Escape && Inspector?.Measurement != null)
        {
            Inspector.ResetMeasurement();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    private void ToggleHierarchy(object? sender, RoutedEventArgs e) => Inspector?.ToggleSelected();
    private void SelectParent(object? sender, RoutedEventArgs e) => Inspector?.SelectParent();
    private void PickCoordinates(object? sender, RoutedEventArgs e) => Inspector?.UseCoordinates();
    private void FindDiagnostic(object? sender, RoutedEventArgs e) { if (Inspector is { } model) model.Query = "Exception"; }
    private void HierarchyKeyDown(object? sender, KeyEventArgs e)
    {
        if (Inspector is not { } model) return;
        if (e.Key is Key.Space or Key.Right) model.ToggleSelected();
        else if (e.Key == Key.Left) model.SelectParent();
        else return;
        e.Handled = true;
        if (sender is ListBox hierarchy && model.Selected is { } selection)
        {
            hierarchy.ScrollIntoView(selection);
            // Revealing/collapsing a node rebuilds the virtualized items. Restore focus only
            // after their containers are realized, and never after a refresh changes selection.
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(model.Selected, selection))
                    hierarchy.ContainerFromItem(selection)?.Focus();
            }, DispatcherPriority.Background);
        }
    }

    private void OpenSource(object? sender, RoutedEventArgs e)
    {
        if (Inspector?.SourceNode is not { } source) return;
        try
        {
            var executable = SourceNavigation.FindExecutable() ?? string.Empty;
            using var process = Process.Start(SourceNavigation.CreateStartInfo(executable, source));
            Inspector.Status = process == null ? "The editor did not start. Use Copy location." : "Opened source in VS Code.";
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { Inspector.Status = error.Message + " Use Copy location if the editor is unavailable."; }
    }

    private async void CopyLocation(object? sender, RoutedEventArgs e)
    {
        if (Inspector is not { } model) return;
        try
        {
            if (Clipboard == null) { model.Status = "Clipboard is unavailable; select and copy the source path above."; return; }
            await Clipboard.SetTextAsync(model.CopyLocation);
            model.Status = "Source location copied.";
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { model.Status = "Could not copy: " + error.Message + " Select and copy the source path above."; }
    }
}

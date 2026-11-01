using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace FossPDF.Previewer;

internal sealed class PreviewerControl : Control
{
    private readonly PreviewViewport viewport = new();
    private PreviewPage[] pages = Array.Empty<PreviewPage>();
    private InspectionViewModel? subscribed;
    private Point previous;
    private bool panning;
    private bool metricsQueued;
    public static readonly StyledProperty<ObservableCollection<PreviewPage>?> PagesProperty =
        AvaloniaProperty.Register<PreviewerControl, ObservableCollection<PreviewPage>?>(nameof(Pages));
    public ObservableCollection<PreviewPage>? Pages { get => GetValue(PagesProperty); set => SetValue(PagesProperty, value); }
    public static readonly StyledProperty<InspectionViewModel?> InspectorProperty =
        AvaloniaProperty.Register<PreviewerControl, InspectionViewModel?>(nameof(Inspector));
    public InspectionViewModel? Inspector { get => GetValue(InspectorProperty); set => SetValue(InspectorProperty, value); }
    public static readonly StyledProperty<float> CurrentScrollProperty = AvaloniaProperty.Register<PreviewerControl, float>(nameof(CurrentScroll));
    public float CurrentScroll { get => GetValue(CurrentScrollProperty); set => SetValue(CurrentScrollProperty, value); }
    public static readonly StyledProperty<float> ScrollViewportSizeProperty = AvaloniaProperty.Register<PreviewerControl, float>(nameof(ScrollViewportSize));
    public float ScrollViewportSize { get => GetValue(ScrollViewportSizeProperty); set => SetValue(ScrollViewportSizeProperty, value); }

    public PreviewerControl() { ClipToBounds = true; Focusable = true; }
    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PagesProperty)
        {
            pages = Pages?.ToArray() ?? Array.Empty<PreviewPage>();
            viewport.SetPages(pages.Select(page => new PreviewPageSize(page.Width, page.Height)));
        }
        if (change.Property == CurrentScrollProperty) viewport.Scroll = CurrentScroll;
        if (change.Property == InspectorProperty) Subscribe(Inspector);
        if (change.Property == PagesProperty || change.Property == CurrentScrollProperty || change.Property == InspectorProperty) InvalidateVisual();
    }

    private void Subscribe(InspectionViewModel? model)
    {
        if (subscribed != null) { subscribed.PropertyChanged -= InspectorChanged; subscribed.FocusRequested -= FocusOccurrence; }
        subscribed = model;
        if (subscribed != null) { subscribed.PropertyChanged += InspectorChanged; subscribed.FocusRequested += FocusOccurrence; }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Subscribe(Inspector); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Subscribe(null); base.OnDetachedFromVisualTree(e); }
    private void InspectorChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
    private void FocusOccurrence(InspectionOccurrence occurrence) { viewport.Focus(occurrence); InvalidateVisual(); }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if ((e.KeyModifiers & KeyModifiers.Control) != 0) viewport.Zoom(e.GetPosition(this), Math.Pow(1.1, e.Delta.Y));
        else viewport.Pan(e.Delta.X * 25, e.Delta.Y * 25);
        UpdatePointer(e.GetPosition(this)); InvalidateVisual(); e.Handled = true;
    }
    private void UpdatePointer(Point point) => Inspector?.UpdatePointer(viewport.TryPick(point, out var value) ? value : null);
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var current = e.GetPosition(this);
        if (panning) viewport.Pan(current.X - previous.X, current.Y - previous.Y);
        previous = current; UpdatePointer(current); InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Inspector?.UpdatePointer(null); InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); previous = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        panning = properties.IsMiddleButtonPressed || properties.IsLeftButtonPressed && (Inspector == null || Inspector.Tool == InspectionViewModel.Tools[0]);
        if (panning) e.Pointer.Capture(this);
        else if (properties.IsLeftButtonPressed && viewport.TryPick(previous, out var point)) Inspector?.Pick(point);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); panning = false; e.Pointer.Capture(null); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); panning = false; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left: viewport.Pan(40, 0); break;
            case Key.Right: viewport.Pan(-40, 0); break;
            case Key.Up: viewport.Pan(0, 40); break;
            case Key.Down: viewport.Pan(0, -40); break;
            case Key.Add: case Key.OemPlus: viewport.Zoom(Bounds.Center, 1.1); break;
            case Key.Subtract: case Key.OemMinus: viewport.Zoom(Bounds.Center, 1 / 1.1); break;
            case Key.Escape: Inspector?.ResetMeasurement(); break;
            default: return;
        }
        e.Handled = true; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        viewport.Width = Bounds.Width; viewport.Height = Bounds.Height; viewport.Clamp();
        // Scroll bindings can invalidate this visual. Publish after the render pass completes.
        if (!metricsQueued)
        {
            metricsQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                metricsQueued = false;
                SetCurrentValue(CurrentScrollProperty, (float)viewport.Scroll);
                SetCurrentValue(ScrollViewportSizeProperty, (float)viewport.ScrollViewport);
            }, DispatcherPriority.Background);
        }
        var frame = new InteractiveCanvas
        {
            Bounds = new Rect(0, 0, Bounds.Width, Bounds.Height), Viewport = viewport.Snapshot(), Pages = pages,
            Highlights = Inspector?.Highlights.ToArray() ?? Array.Empty<InspectionOccurrence>(),
            Measurement = Inspector?.Measurement, Pointer = Inspector?.Hovered, Magnify = Inspector?.Magnify == true
        };
        frame.RetainPictures();
        try { context.Custom(frame); }
        catch { frame.Dispose(); throw; }
        base.Render(context);
    }
}

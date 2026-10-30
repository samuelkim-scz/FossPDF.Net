using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace FossPDF.Previewer
{
    class PreviewerControl : Control
    {
        private InteractiveCanvas InteractiveCanvas { get; set; } = new ();
        public static readonly StyledProperty<InspectionViewModel?> InspectorProperty = AvaloniaProperty.Register<PreviewerControl, InspectionViewModel?>(nameof(Inspector));
        public InspectionViewModel? Inspector { get => GetValue(InspectorProperty); set => SetValue(InspectorProperty, value); }
        public static readonly StyledProperty<InspectionOccurrence?> HighlightProperty = AvaloniaProperty.Register<PreviewerControl, InspectionOccurrence?>(nameof(Highlight));
        public InspectionOccurrence? Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
        public static readonly StyledProperty<InspectionMeasurement?> MeasurementProperty = AvaloniaProperty.Register<PreviewerControl, InspectionMeasurement?>(nameof(Measurement));
        public InspectionMeasurement? Measurement { get => GetValue(MeasurementProperty); set => SetValue(MeasurementProperty, value); }
        public static readonly StyledProperty<double> ZoomPercentProperty = AvaloniaProperty.Register<PreviewerControl, double>(nameof(ZoomPercent), 100);
        public double ZoomPercent { get => GetValue(ZoomPercentProperty); set => SetValue(ZoomPercentProperty, value); }
        
        public static readonly StyledProperty<ObservableCollection<PreviewPage>?> PagesProperty =
            AvaloniaProperty.Register<PreviewerControl, ObservableCollection<PreviewPage>?>(nameof(Pages));
        
        public ObservableCollection<PreviewPage>? Pages
        {
            get => GetValue(PagesProperty);
            set => SetValue(PagesProperty, value);
        }

        public static readonly StyledProperty<float> CurrentScrollProperty = AvaloniaProperty.Register<PreviewerControl, float>(nameof(CurrentScroll));
        
        public float CurrentScroll
        {
            get => GetValue(CurrentScrollProperty);
            set => SetValue(CurrentScrollProperty, value);
        }
        
        public static readonly StyledProperty<float> ScrollViewportSizeProperty = AvaloniaProperty.Register<PreviewerControl, float>(nameof(ScrollViewportSize));
        
        public float ScrollViewportSize
        {
            get => GetValue(ScrollViewportSizeProperty);
            set => SetValue(ScrollViewportSizeProperty, value);
        }
        
        public PreviewerControl()
        {
            HighlightProperty.Changed.Subscribe(change =>
            {
                InteractiveCanvas.Highlight = change.NewValue.Value;
                if (InteractiveCanvas.Highlight != null) InteractiveCanvas.Reveal(InteractiveCanvas.Highlight);
                InvalidateVisual();
            });
            MeasurementProperty.Changed.Subscribe(change => { InteractiveCanvas.Measurement = change.NewValue.Value; InvalidateVisual(); });
            ZoomPercentProperty.Changed.Subscribe(change =>
            {
                InteractiveCanvas.ZoomToPoint(0, (float)Bounds.Height / 2, (float)(change.NewValue.Value / 100 / InteractiveCanvas.Scale));
                InvalidateVisual();
            });
            PagesProperty.Changed.Subscribe(x =>
            {
                InteractiveCanvas.Pages = x.NewValue.Value ?? (ICollection<PreviewPage>)Array.Empty<PreviewPage>();
                InvalidateVisual();
            });

            CurrentScrollProperty.Changed.Subscribe(x =>
            {
                InteractiveCanvas.ScrollPercentY = x.NewValue.Value;
                Dispatcher.UIThread.InvokeAsync(InvalidateVisual).GetTask();
            });

            ClipToBounds = true;
        }
        
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                var scaleFactor = 1 + e.Delta.Y / 10f;
                var point = new Point(Bounds.Center.X, Bounds.Top) - e.GetPosition(this);
                
                InteractiveCanvas.ZoomToPoint((float)point.X, -(float)point.Y, (float)scaleFactor);
                ZoomPercent = InteractiveCanvas.Scale * 100;
            }
                
            if (e.KeyModifiers == KeyModifiers.None)
            {
                var translation = (float)e.Delta.Y * 25;
                InteractiveCanvas.TranslateWithCurrentScale(0, -translation);
            }
                
            InvalidateVisual();
        }

        private bool IsMousePressed { get; set; }
        private Vector MousePosition { get; set; }
        
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (IsMousePressed)
            {
                var currentPosition = e.GetPosition(this);
                var translation = currentPosition - MousePosition;
                InteractiveCanvas.TranslateWithCurrentScale((float)translation.X, -(float)translation.Y);
                
                InvalidateVisual();
            }

            MousePosition = e.GetPosition(this);
            var point = InteractiveCanvas.PagePoint((float)MousePosition.X, (float)MousePosition.Y);
            if (point.HasValue) Inspector?.SetPointer(point.Value.Page, point.Value.X, point.Value.Y);
        }
        
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            MousePosition = e.GetPosition(this);
            if (Inspector?.InteractionMode > 0)
            {
                var point = InteractiveCanvas.PagePoint((float)MousePosition.X, (float)MousePosition.Y);
                if (point.HasValue) Inspector.Pick(point.Value.Page, point.Value.X, point.Value.Y);
                InvalidateVisual(); e.Handled = true; return;
            }
            IsMousePressed = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            IsMousePressed = false;
        }

        public override void Render(DrawingContext context)
        {
            CurrentScroll = InteractiveCanvas.ScrollPercentY;
            ScrollViewportSize = InteractiveCanvas.ScrollViewportSizeY;
    
            InteractiveCanvas.Bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

            context.Custom(InteractiveCanvas);
            base.Render(context);
        }
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using Avalonia.Threading;

namespace FossPDF.Previewer
{
    internal class PreviewerWindowViewModel : ReactiveObject, IDisposable
    {
        private bool disposed;
        private InspectionSnapshot? inspection;
        public InspectionSnapshot? Inspection
        {
            get => inspection;
            private set => this.RaiseAndSetIfChanged(ref inspection, value);
        }
        private ObservableCollection<PreviewPage> _pages = new();
        public ObservableCollection<PreviewPage> Pages
        {
            get => _pages;
            set => this.RaiseAndSetIfChanged(ref _pages, value);
        }
        
        private float _currentScroll;
        public float CurrentScroll
        {
            get => _currentScroll;
            set => this.RaiseAndSetIfChanged(ref _currentScroll, value);
        }

        private float _scrollViewportSize;
        public float ScrollViewportSize
        {
            get => _scrollViewportSize;
            set
            {
                this.RaiseAndSetIfChanged(ref _scrollViewportSize, value);
                VerticalScrollbarVisible = value < 1;
            }
        }

        private bool _verticalScrollbarVisible;
        public bool VerticalScrollbarVisible
        {
            get => _verticalScrollbarVisible;
            private set => Dispatcher.UIThread.Post(() => this.RaiseAndSetIfChanged(ref _verticalScrollbarVisible, value));
        }

        public ReactiveCommand<Unit, Unit> ShowPdfCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDocumentationCommand { get; }
        public ReactiveCommand<Unit, Unit> SponsorProjectCommand { get; }

        public PreviewerWindowViewModel()
        {
            CommunicationService.Instance.OnDocumentRefreshed += HandleUpdatePreview;
            CommunicationService.Instance.OnInspectionRefreshed += HandleInspection;
            
            ShowPdfCommand = ReactiveCommand.Create(ShowPdf);
            ShowDocumentationCommand = ReactiveCommand.Create(() => OpenLink("https://www.FossPDF.com/api-reference/index.html"));
            SponsorProjectCommand = ReactiveCommand.Create(() => OpenLink("https://github.com/sponsors/FossPDF"));
        }

        private void ShowPdf()
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
            Helpers.GeneratePdfFromDocumentSnapshots(filePath, Pages);

            OpenLink(filePath);
        }
        
        private void OpenLink(string path)
        {
            using var openBrowserProcess = new Process
            {
                StartInfo = new()
                {
                    UseShellExecute = true,
                    FileName = path
                }
            };

            openBrowserProcess.Start();
        }
        
        private void HandleUpdatePreview(ICollection<PreviewPage> pages)
        {
            if (disposed)
            {
                foreach (var page in pages) page.Picture.Dispose();
                return;
            }
            var oldPages = Pages;
            Pages = new ObservableCollection<PreviewPage>(pages);
            
            foreach (var page in oldPages)
                page.Picture.Dispose();
            oldPages.Clear();
        }

        private void HandleInspection(InspectionSnapshot? snapshot) => Inspection = snapshot;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CommunicationService.Instance.OnDocumentRefreshed -= HandleUpdatePreview;
            CommunicationService.Instance.OnInspectionRefreshed -= HandleInspection;
            foreach (var page in Pages) page.Picture.Dispose();
            Pages.Clear(); Inspection = null;
            ShowPdfCommand.Dispose(); ShowDocumentationCommand.Dispose(); SponsorProjectCommand.Dispose();
        }
    }
}

#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Previewer
{
    public static class Extensions
    {
        public static void ShowInPreviewer(this IDocument document, int port = 12500)
        {
            document.ShowInPreviewerAsync(port).ConfigureAwait(true).GetAwaiter().GetResult();
        }

        public static void ShowInPreviewer(this IDocument document, PreviewerOptions options, int port = 12500)
            => document.ShowInPreviewerAsync(options, port).ConfigureAwait(false).GetAwaiter().GetResult();
        
        public static Task ShowInPreviewerAsync(this IDocument document, int port = 12500)
            => document.ShowInPreviewerAsync(new PreviewerOptions(), port);

        public static async Task ShowInPreviewerAsync(this IDocument document, PreviewerOptions options, int port = 12500)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            using var previewerService = new PreviewerService(port);
            var refreshGate = new object();
            var pendingRefresh = Task.CompletedTask;
            var closing = false;
            Exception? refreshFailure = null;
            
            using var cancellationTokenSource = new CancellationTokenSource();
            Action stoppedHandler = () =>
            {
                lock (refreshGate)
                    if (!closing) cancellationTokenSource.Cancel();
            };
            previewerService.OnPreviewerStopped += stoppedHandler;
            
            try
            {
                await previewerService.Connect();
                if (options.EnableInspection) await previewerService.RequireInspectionSupport();
                await RefreshPreview();
            }
            catch
            {
                lock (refreshGate) closing = true;
                previewerService.OnPreviewerStopped -= stoppedHandler;
                throw;
            }
            
            EventHandler refreshHandler = (_, _) =>
            {
                lock (refreshGate)
                {
                    if (!closing) pendingRefresh = RefreshAfter(pendingRefresh);
                }
            };
            HotReloadManager.UpdateApplicationRequested += refreshHandler;
            try
            {
                try { await WaitForPreviewerExit(cancellationTokenSource.Token); }
                catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested) { }
            }
            finally
            {
                HotReloadManager.UpdateApplicationRequested -= refreshHandler;
                previewerService.OnPreviewerStopped -= stoppedHandler;
                Task remaining;
                lock (refreshGate) { closing = true; remaining = pendingRefresh; }
                await remaining;
            }
            if (refreshFailure != null)
                throw new InvalidOperationException("The preview refresh failed.", refreshFailure);

            async Task RefreshAfter(Task previous)
            {
                try { await previous; await RefreshPreview(); }
                catch (Exception error)
                {
                    refreshFailure = error;
                    cancellationTokenSource.Cancel();
                }
            }
            
            async Task RefreshPreview()
            {
                if (cancellationTokenSource.IsCancellationRequested) return;
                using var snapshot = GetSnapshot();
                await previewerService.RefreshPreview(snapshot);

                PreviewerSnapshot GetSnapshot()
                {
                    try
                    {
                        return document.GeneratePreviewerSnapshot(options);
                    }
                    catch (Exception exception)
                    {
                        var exceptionDocument = new ExceptionDocument(exception);
                        // A failed render replaces obsolete inspection with an ordinary error preview.
                        return exceptionDocument.GeneratePreviewerSnapshot();
                    }
                }
            }

            async Task WaitForPreviewerExit(CancellationToken cancellationToken)
            {
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }
    }
}

#endif

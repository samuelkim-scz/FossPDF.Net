#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.Fluent;
using System.Linq;

namespace FossPDF.Previewer
{
    public static class Extensions
    {
        public static void ShowInPreviewer(this IDocument document, int port = 12500)
        {
            document.ShowInPreviewerAsync(port).ConfigureAwait(true).GetAwaiter().GetResult();
        }
        
        public static async Task ShowInPreviewerAsync(this IDocument document, int port = 12500)
            => await document.ShowInPreviewerAsync(port, false);

        /// <summary>Opt in to local source/text inspection; the previewer must support protocol 1.</summary>
        public static void ShowInPreviewer(this IDocument document, int port, bool enableInspection)
            => document.ShowInPreviewerAsync(port, enableInspection).ConfigureAwait(true).GetAwaiter().GetResult();

        public static async Task ShowInPreviewerAsync(this IDocument document, int port, bool enableInspection)
        {
            using var previewerService = new PreviewerService(port);
            
            using var cancellationTokenSource = new CancellationTokenSource();
            previewerService.OnPreviewerStopped += () => cancellationTokenSource.Cancel();
            
            await previewerService.Connect();
            if (enableInspection)
                await previewerService.RequireInspectionSupport();
            await RefreshPreview();
            
            EventHandler refreshHandler = async (_, _) => await RefreshPreview();
            HotReloadManager.UpdateApplicationRequested += refreshHandler;
            try
            {
                await WaitForPreviewerExit(cancellationTokenSource.Token);
            }
            finally
            {
                HotReloadManager.UpdateApplicationRequested -= refreshHandler;
            }
            
            async Task RefreshPreview()
            {
                if (enableInspection)
                {
                    try
                    {
                        using var preview = document.GenerateInspectedPreview();
                        await previewerService.RefreshPreview(preview.Pictures.ToArray(), preview.Inspection);
                    }
                    catch (Exception exception)
                    {
                        using var error = new ExceptionDocument(exception).GenerateInspectedPreview();
                        var diagnostic = exception.ToString();
                        error.Inspection.Diagnostic = diagnostic.Length <= 32768 ? diagnostic : diagnostic.Substring(0, 32768);
                        await previewerService.RefreshPreview(error.Pictures.ToArray(), error.Inspection);
                    }
                    return;
                }
                var pictures = GetPictures();
                await previewerService.RefreshPreview(pictures);
                
                ICollection<PreviewerPicture> GetPictures()
                {
                    try
                    {
                        return DocumentGenerator.GeneratePreviewerPictures(document);
                    }
                    catch (Exception exception)
                    {
                        var exceptionDocument = new ExceptionDocument(exception);
                        return DocumentGenerator.GeneratePreviewerPictures(exceptionDocument);
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

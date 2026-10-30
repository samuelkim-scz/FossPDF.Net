#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Drawing;

namespace FossPDF.Previewer
{
    internal sealed class PreviewerService : IDisposable
    {
        private int Port { get; }
        private HttpClient HttpClient { get; }


        private const int RequiredPreviewerVersionMajor = 2026;
        private const int RequiredPreviewerVersionMinor = 1;

        public PreviewerService(int port)
        {
            Port = port;
            HttpClient = new()
            {
                BaseAddress = new Uri($"http://localhost:{port}/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public void Dispose() => HttpClient.Dispose();

        public async Task Connect()
        {
            var isAvailable = await IsPreviewerAvailable();

            if (!isAvailable)
            {
                StartPreviewer();
                await WaitForConnection();
            }

            var previewerVersion = await GetPreviewerVersion();
            CheckVersionCompatibility(previewerVersion);
        }

        internal async Task RequireInspectionSupport()
        {
            using var response = await HttpClient.GetAsync("/inspection/version");
            if (!response.IsSuccessStatusCode || await response.Content.ReadFromJsonAsync<int>() != InspectionSnapshot.CurrentVersion)
                throw new NotSupportedException("This previewer does not support inspection protocol 1. Update FossPDF.Previewer or disable inspection.");
        }

        internal async Task WaitForExit(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && await IsPreviewerAvailable())
            {
                try { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            }
        }

        private async Task<bool> IsPreviewerAvailable()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var result = await HttpClient.GetAsync("/ping", timeout.Token);
                return result.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<Version> GetPreviewerVersion()
        {
            using var result = await HttpClient.GetAsync("/version");
            return await result.Content.ReadFromJsonAsync<Version>()
                ?? throw new InvalidOperationException("The previewer did not return a version.");
        }

        private void StartPreviewer()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new()
                    {
                        FileName = "FossPDF-previewer",
                        Arguments = $"{Port}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                Task.Run(async () =>
                {
                    await process.WaitForExitAsync();
                    process.Dispose();
                });
            }
            catch
            {
                throw new Exception("Cannot start the FossPDF Previewer tool. " +
                                    "Please install it by executing in terminal the following command: 'dotnet tool install --global FossPDF.Previewer'.");
            }
        }

        private void CheckVersionCompatibility(Version version)
        {
            if (version.Major == RequiredPreviewerVersionMajor && version.Minor >= RequiredPreviewerVersionMinor)
                return;

            throw new Exception($"Previewer version is not compatible. Possible solutions: " +
                                $"1) Update the FossPDF library to newer version. " +
                                $"2) Update the FossPDF previewer tool using the following command: 'dotnet tool update --global FossPDF.Previewer --version {RequiredPreviewerVersionMajor}.{RequiredPreviewerVersionMinor}'");
        }

        private async Task WaitForConnection()
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));

            var cancellationToken = cancellationTokenSource.Token;

            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));

                if (cancellationToken.IsCancellationRequested)
                    throw new Exception($"Cannot connect to the FossPDF Previewer tool. Please make sure that your Operating System does not block HTTP connections on port {Port}.");

                var isConnected = await IsPreviewerAvailable();

                if (isConnected)
                    break;
            }
        }

        public async Task RefreshPreview(ICollection<PreviewerPicture> pictures, InspectionSnapshot? inspection = null)
        {
            try
            {
                using var multipartContent = new MultipartFormDataContent();
                var pages = new List<PreviewerRefreshCommand.Page>();
                foreach (var picture in pictures)
                {
                    var page = new PreviewerRefreshCommand.Page { Width = picture.Size.Width, Height = picture.Size.Height };
                    pages.Add(page);
                    using var pictureData = picture.Picture.Serialize();
                    multipartContent.Add(new ByteArrayContent(pictureData.ToArray()), page.Id, page.Id);
                }
                var command = new PreviewerRefreshCommand { Pages = pages, Inspection = inspection };
                multipartContent.Add(JsonContent.Create(command), "command");
                using var response = await HttpClient.PostAsync("/update/preview", multipartContent);
                response.EnsureSuccessStatusCode();
            }
            finally
            {
                foreach (var picture in pictures)
                    picture.Picture.Dispose();
            }
        }
    }
}

#endif

#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Drawing;

namespace FossPDF.Previewer
{
    internal sealed class PreviewerService : IDisposable
    {
        private int Port { get; }
        private HttpClient HttpClient { get; }
        private readonly HttpClient? OwnedHttpClient;

        public  event Action? OnPreviewerStopped;

        private const int RequiredPreviewerVersionMajor = 2026;
        private const int RequiredPreviewerVersionMinor = 1;

        public PreviewerService(int port)
        {
            Port = port;
            OwnedHttpClient = new()
            {
                BaseAddress = new Uri($"http://localhost:{port}/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            HttpClient = OwnedHttpClient;
        }

        internal PreviewerService(HttpClient httpClient) { HttpClient = httpClient; }

        public void Dispose() => OwnedHttpClient?.Dispose();

        internal async Task RequireInspectionSupport()
        {
            using var response = await HttpClient.GetAsync("/capabilities");
            if (!response.IsSuccessStatusCode)
                throw new NotSupportedException("This previewer does not support inspection version 1. Update FossPDF.Previewer or disable inspection.");
            var capabilities = await response.Content.ReadFromJsonAsync<Capabilities>();
            if (capabilities?.InspectionVersions?.Contains(PreviewerProtocol.Version) != true)
                throw new NotSupportedException("The previewer has no compatible inspection version. Update FossPDF.Previewer or disable inspection.");
        }

        private sealed class Capabilities
        {
            public int[]? InspectionVersions { get; set; }
        }

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

        private async Task<bool> IsPreviewerAvailable()
        {
            try
            {
                using var result = await HttpClient.GetAsync("/ping");
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
                    OnPreviewerStopped?.Invoke();
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

        public async Task RefreshPreview(ICollection<PreviewerPicture> pictures)
        {
            using var snapshot = new PreviewerSnapshot(pictures, null);
            await RefreshPreview(snapshot);
        }

        internal async Task RefreshPreview(PreviewerSnapshot snapshot)
        {
            using var multipartContent = new MultipartFormDataContent();

            var pages = new List<PreviewerRefreshCommand.Page>();

            long totalBytes = 0;
            foreach (var picture in snapshot.Pictures)
            {
                var page = new PreviewerRefreshCommand.Page
                {
                    Width = picture.Size.Width,
                    Height = picture.Size.Height
                };

                pages.Add(page);

                using var pictureData = picture.Picture.Serialize();
                totalBytes += pictureData.Size;
                if (totalBytes > PreviewerProtocol.MaxRequestBytes - PreviewerProtocol.MaxCommandBytes)
                    throw new InvalidOperationException("Preview pictures exceed the request size limit.");
                multipartContent.Add(new ByteArrayContent(pictureData.ToArray()), page.Id, page.Id);
            }

            var command = new PreviewerRefreshCommand
            {
                Pages = pages,
                ProtocolVersion = snapshot.Inspection == null ? 0 : PreviewerProtocol.Version,
                Inspection = snapshot.Inspection
            };

            if (snapshot.Inspection != null) PreviewerProtocol.Validate(snapshot.Inspection, pages.Count);
            if (pages.Count > PreviewerProtocol.MaxPages) throw new InvalidOperationException("Too many preview pages.");
            var json = JsonSerializer.SerializeToUtf8Bytes(command);
            if (json.Length > PreviewerProtocol.MaxCommandBytes)
                throw new InvalidOperationException("Preview command exceeds the 16 MiB limit.");
            multipartContent.Add(new ByteArrayContent(json), "command");

            using var response = await HttpClient.PostAsync("/update/preview", multipartContent);
            response.EnsureSuccessStatusCode();
        }
    }
}

#endif

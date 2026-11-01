#if NET9_0
#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using FossPDF.Fluent;
using FossPDF.Previewer;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    public class InspectionTransportTests
    {
        [Test]
        public async Task WindowRefreshDisposesOldPicturesAndClearsTheInspectorForLegacyClients()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var service = new CommunicationService(action => { action(); return Task.CompletedTask; });
            var window = new PreviewerWindowViewModel(service, action => action());
            var running = service.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri("http://localhost:" + port) };
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            var snapshot = preview.Inspection;
            async Task Send(bool inspected)
            {
                using var form = new MultipartFormDataContent();
                var pages = preview.Pictures.Select((picture, index) => new { Id = "p" + index, Width = picture.Size.Width, Height = picture.Size.Height }).ToArray();
                form.Add(JsonContent.Create(new { Pages = pages, Inspection = inspected ? snapshot : null }), "command");
                for (var index = 0; index < preview.Pictures.Count; index++)
                {
                    using var data = preview.Pictures[index].Picture.Serialize();
                    form.Add(new ByteArrayContent(data.ToArray()), "p" + index, "p" + index);
                }
                using var response = await client.PostAsync("update/preview", form);
                response.EnsureSuccessStatusCode();
            }
            try
            {
                var oldPages = Array.Empty<PreviewPage>();
                for (var index = 0; index < 20; index++)
                {
                    await Send(true);
                    Assert.That(oldPages.All(page => page.Picture.Handle == IntPtr.Zero), Is.True);
                    oldPages = window.Pages.ToArray();
                    window.Inspector.Query = "Report heading";
                    await window.Inspector.SearchCompletion;
                    window.Inspector.Selected = window.Inspector.Entries.Single();
                    Assert.That(window.Inspector.Highlights.Count, Is.EqualTo(window.Pages.Count));
                }
                await Send(false);
                Assert.That(window.Inspector.Entries, Is.Empty);
                Assert.That(window.Inspector.Highlights, Is.Empty);
                Assert.That(window.Inspector.SourceNode, Is.Null);
                oldPages = window.Pages.ToArray();
                window.Dispose(); window.Dispose();
                Assert.That(oldPages.All(page => page.Picture.Handle == IntPtr.Zero), Is.True);
                Assert.That(window.Pages, Is.Empty);
                using var ping = await client.GetAsync("ping");
                Assert.That(ping.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            }
            finally { window.Dispose(); await service.Stop(); await running; }
        }

        [Test]
        public async Task LegacyAndInspectionUpdatesRoundTripAndMalformedUpdatesAreAtomic()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var service = new CommunicationService(action => { action(); return Task.CompletedTask; });
            var updates = 0;
            InspectionSnapshot? accepted = null;
            service.OnDocumentRefreshed += pages => { updates++; foreach (var page in pages) page.Picture.Dispose(); };
            service.OnInspectionRefreshed += snapshot => accepted = snapshot;
            var running = service.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri("http://localhost:" + port) };
            try
            {
                for (var retry = 0; ; retry++)
                {
                    try { using var ping = await client.GetAsync("ping"); if (ping.IsSuccessStatusCode) break; }
                    catch (HttpRequestException) when (retry < 100) { }
                    if (retry >= 100) Assert.Fail("Inspection server did not start.");
                    await Task.Delay(20);
                }
                Assert.That(await client.GetFromJsonAsync<int>("inspection/version"), Is.EqualTo(1));
                using var preview = Document.Create(container => container.Page(page => page.Content().Inspect("revenue").Text("Revenue"))).GenerateInspectedPreview();
                using var data = preview.Pictures[0].Picture.Serialize();
                var bytes = data.ToArray();
                async Task<HttpStatusCode> Send(InspectionSnapshot? snapshot, string id = "p1", float width = 100)
                {
                    using var form = new MultipartFormDataContent();
                    var pages = new[] { new { Id = id, Width = width, Height = 100 } };
                    if (snapshot == null)
                        form.Add(JsonContent.Create(new { Pages = pages }), "command");
                    else
                        form.Add(JsonContent.Create(new { Pages = pages, Inspection = snapshot }), "command");
                    form.Add(new ByteArrayContent(bytes), "p1", "p1");
                    using var response = await client.PostAsync("update/preview", form);
                    return response.StatusCode;
                }
                Assert.That(await Send(null), Is.EqualTo(HttpStatusCode.OK));
                Assert.That(accepted, Is.Null);
                for (var index = 0; index < 20; index++)
                    Assert.That(await Send(preview.Inspection), Is.EqualTo(HttpStatusCode.OK));
                Assert.That(accepted!.Nodes.Any(x => x.Id.Contains("revenue")), Is.True);
                var before = updates;
                Assert.That(await Send(new InspectionSnapshot { Version = 999 }), Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(await Send(preview.Inspection, width: -1), Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(await Send(preview.Inspection, id: "missing-file"), Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(updates, Is.EqualTo(before));
                Assert.That(await Send(null), Is.EqualTo(HttpStatusCode.OK));
                Assert.That(accepted, Is.Null, "A legacy refresh must clear obsolete inspection metadata.");
            }
            finally { await service.Stop(); await running; }
        }
    }
}
#endif

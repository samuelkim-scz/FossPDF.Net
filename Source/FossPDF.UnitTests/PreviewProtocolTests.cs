#if NET9_0_OR_GREATER
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Previewer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class PreviewProtocolTests
    {
        private static async Task<int?> Send(CommunicationService server, object command, params byte[][] pictures)
        {
            var context = new DefaultHttpContext();
            context.Request.ContentType = "multipart/form-data; boundary=test";
            var files = new FormFileCollection();
            for (var index = 0; index < pictures.Length; index++)
                files.Add(new FormFile(new MemoryStream(pictures[index]), 0, pictures[index].Length, "p" + index, "p" + index));
            context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            { ["command"] = JsonSerializer.Serialize(command) }, files);
            var result = await server.HandleUpdatePreview(context.Request);
            return (result as IStatusCodeHttpResult)?.StatusCode;
        }

        [Test]
        public async Task LegacyAndVersionOneRefreshesRoundTripAndDisposePreviousPictures()
        {
            var server = new CommunicationService { Dispatch = action => { action(); return Task.CompletedTask; } };
            PreviewUpdate? current = null;
            server.OnDocumentRefreshed += update => { var old = current; current = update; old?.Dispose(); };
            using var source = PreviewInspectionTests.Sample(1).GeneratePreviewerSnapshot(PreviewInspectionTests.Enabled());
            using var data = source.Pictures.First().Picture.Serialize();
            var pages = new[] { new { Id = "p0", Width = 200, Height = 180 } };
            try
            {
                Assert.That(await Send(server, new { Pages = pages }, data.ToArray()), Is.EqualTo(200));
                Assert.That(current!.Inspection, Is.Null);
                for (var index = 0; index < 20; index++)
                {
                    var previous = current;
                    Assert.That(await Send(server, new { ProtocolVersion = 1, Pages = pages, Inspection = source.Inspection }, data.ToArray()), Is.EqualTo(200));
                    Assert.That(previous!.Pages.All(x => x.Picture.Handle == IntPtr.Zero), Is.True);
                    Assert.That(current!.Inspection!.Elements.Count, Is.EqualTo(source.Inspection!.Elements.Count));
                }
                var lastInspection = current;
                Assert.That(await Send(server, new { Pages = pages }, data.ToArray()), Is.EqualTo(200));
                Assert.That(current!.Inspection, Is.Null, "Error/legacy previews must clear old source data.");
                Assert.That(lastInspection!.Pages.All(x => x.Picture.Handle == IntPtr.Zero), Is.True);
            }
            finally { current?.Dispose(); }
        }

        [Test]
        public async Task MalformedRequestsCannotReplaceCurrentPreview()
        {
            var server = new CommunicationService { Dispatch = action => { action(); return Task.CompletedTask; } };
            var refreshes = 0;
            server.OnDocumentRefreshed += _ => refreshes++;
            var pages = new[] { new { Id = "p0", Width = 200, Height = 180 } };
            Assert.That(await Send(server, new { ProtocolVersion = 2, Pages = pages }), Is.EqualTo(400));
            Assert.That(await Send(server, new { Pages = pages }), Is.EqualTo(400));
            Assert.That(await Send(server, new { Pages = pages }, new byte[] { 1, 2, 3 }), Is.EqualTo(400));
            Assert.That(await Send(server, new { Pages = new[] { new { Id = "p0", Width = -1, Height = 5 } } }, Array.Empty<byte>()), Is.EqualTo(400));
            Assert.That(await Send(server, new { Pages = pages.Concat(pages).ToArray() }), Is.EqualTo(400));
            var snapshot = new InspectionSnapshot { Version = 2 };
            Assert.Throws<ArgumentException>(() => PreviewerProtocol.Validate(snapshot, 1));
            snapshot.Version = 1;
            snapshot.Elements.Add(new InspectionElement { Id = "child", ParentId = "missing", Type = "Text" });
            Assert.Throws<ArgumentException>(() => PreviewerProtocol.Validate(snapshot, 1));
            snapshot.Elements[0].ParentId = null;
            snapshot.Occurrences.Add(new InspectionOccurrence { ElementId = "child", PageNumber = 1, Transform = new float[6], X = float.NaN });
            Assert.Throws<ArgumentException>(() => PreviewerProtocol.Validate(snapshot, 1));
            snapshot.Occurrences[0].X = 0;
            snapshot.Occurrences[0].PageNumber = 2;
            Assert.Throws<ArgumentException>(() => PreviewerProtocol.Validate(snapshot, 1));
            Assert.That(refreshes, Is.Zero);
        }

        [Test]
        public async Task PartialDecodeAndFailedDispatchDisposeUnacceptedPictures()
        {
            using var source = PreviewInspectionTests.Sample(1).GeneratePreviewerSnapshot();
            using var data = source.Pictures.First().Picture.Serialize();
            var decoded = new List<SkiaSharp.SKPicture>();
            var server = new CommunicationService
            {
                Dispatch = _ => throw new InvalidOperationException("Dispatcher stopped"),
                DecodePicture = stream =>
                {
                    var picture = SkiaSharp.SKPicture.Deserialize(stream);
                    if (picture != null) decoded.Add(picture);
                    return picture;
                }
            };
            server.OnDocumentRefreshed += _ => Assert.Fail("Rejected update must not be published");
            var pages = new[] { new { Id = "p0", Width = 200, Height = 180 }, new { Id = "p1", Width = 200, Height = 180 } };
            Assert.That(await Send(server, new { Pages = pages }, data.ToArray(), new byte[] { 1, 2, 3 }), Is.EqualTo(400));
            Assert.That(decoded.Count, Is.EqualTo(1));
            Assert.That(decoded.All(x => x.Handle == IntPtr.Zero), Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Send(server, new { Pages = pages.Take(1).ToArray() }, data.ToArray()));
            Assert.That(decoded.Count, Is.EqualTo(2));
            Assert.That(decoded.All(x => x.Handle == IntPtr.Zero), Is.True);
        }

        [Test]
        public async Task ViewerReplacementAndWindowDisposalReleaseTheCurrentUpdate()
        {
            var server = CommunicationService.Instance;
            var dispatch = server.Dispatch;
            server.Dispatch = action => { action(); return Task.CompletedTask; };
            using var source = PreviewInspectionTests.Sample(1).GeneratePreviewerSnapshot(PreviewInspectionTests.Enabled());
            using var data = source.Pictures.First().Picture.Serialize();
            var model = new PreviewerWindowViewModel();
            var pages = new[] { new { Id = "p0", Width = 200, Height = 180 } };
            try
            {
                Assert.That(await Send(server, new { ProtocolVersion = 1, Pages = pages, Inspection = source.Inspection }, data.ToArray()), Is.EqualTo(200));
                var firstPicture = model.Pages.Single().Picture;
                Assert.That(model.Inspection, Is.Not.Null);
                Assert.That(await Send(server, new { Pages = pages }, data.ToArray()), Is.EqualTo(200));
                Assert.That(model.Inspection, Is.Null);
                Assert.That(firstPicture.Handle, Is.EqualTo(IntPtr.Zero));
                var currentPicture = model.Pages.Single().Picture;
                model.Dispose();
                Assert.That(currentPicture.Handle, Is.EqualTo(IntPtr.Zero));
                Assert.That(model.Pages, Is.Empty);
                Assert.That(await Send(server, new { Pages = pages }, data.ToArray()), Is.EqualTo(503));
            }
            finally { model.Dispose(); server.Dispatch = dispatch; }
        }

        [Test]
        public async Task LiveLoopbackHttpNegotiatesAndTransfersInspection()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var server = new CommunicationService { Dispatch = action => { action(); return Task.CompletedTask; } };
            PreviewUpdate? update = null;
            server.OnDocumentRefreshed += value => update = value;
            var running = server.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            using var service = new PreviewerService(client);
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { using var response = await client.GetAsync("/ping"); response.EnsureSuccessStatusCode(); break; }
                    catch (HttpRequestException) when (attempt < 50) { await Task.Delay(20); }
                }
                await service.RequireInspectionSupport();
                using var snapshot = PreviewInspectionTests.Sample(1).GeneratePreviewerSnapshot(PreviewInspectionTests.Enabled());
                await service.RefreshPreview(snapshot);
                Assert.That(update, Is.Not.Null);
                Assert.That(update!.Inspection!.Elements.Select(x => x.Id), Is.EqualTo(snapshot.Inspection!.Elements.Select(x => x.Id)));
                Assert.That(update.Pages.Count, Is.EqualTo(1));
                using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/update/preview");
                crossOrigin.Headers.Add("Origin", "https://example.test");
                using var rejected = await client.SendAsync(crossOrigin);
                Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            }
            finally { update?.Dispose(); await server.Stop(); await running; }
        }

        [Test]
        public void UnsupportedInspectionNegotiationIsClearAndDoesNotUpload()
        {
            using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:12500") };
            using var service = new PreviewerService(client);
            Assert.That(async () => await service.RequireInspectionSupport(), Throws.TypeOf<NotSupportedException>().With.Message.Contains("disable inspection"));
            Assert.That(handler.Methods, Is.EqualTo(new[] { HttpMethod.Get }));
        }

        [Test]
        public async Task FailedUploadsAndSerializationDoNotLeakCallerOwnedSnapshots()
        {
            using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:12500") };
            using var service = new PreviewerService(client);
            var snapshot = PreviewInspectionTests.Sample(1).GeneratePreviewerSnapshot(PreviewInspectionTests.Enabled());
            var pictures = snapshot.Pictures.Select(x => x.Picture).ToArray();
            try { Assert.ThrowsAsync<HttpRequestException>(async () => await service.RefreshPreview(snapshot)); }
            finally { snapshot.Dispose(); }
            Assert.That(pictures.All(x => x.Handle == IntPtr.Zero), Is.True);
            Assert.That(handler.Methods, Is.EqualTo(new[] { HttpMethod.Post }));
            await Task.CompletedTask;
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> Respond;
            internal List<HttpMethod> Methods { get; } = new();
            internal Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) => Respond = respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            { Methods.Add(request.Method); return Task.FromResult(Respond(request)); }
        }
    }
}
#endif

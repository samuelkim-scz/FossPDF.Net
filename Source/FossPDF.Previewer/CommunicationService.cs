using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Avalonia.Threading;

namespace FossPDF.Previewer;

class CommunicationService
{
    public static CommunicationService Instance { get; } = new ();
    
    public event Action<PreviewUpdate>? OnDocumentRefreshed;

    // Injectable dispatch keeps validation/ownership testable without starting a desktop window.
    internal Func<Action, Task> Dispatch { get; set; } = async action =>
        await Dispatcher.UIThread.InvokeAsync(action);
    internal Func<Stream, SKPicture?> DecodePicture { get; set; } = SKPicture.Deserialize;

    private WebApplication? Application { get; set; }

    private readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal CommunicationService()
    {
        
    }
    
    public Task Start(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging(x => x.ClearProviders());
        builder.WebHost.UseKestrel(options => options.Limits.MaxRequestBodySize = PreviewerProtocol.MaxRequestBytes);
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = PreviewerProtocol.MaxRequestBytes;
            options.ValueLengthLimit = PreviewerProtocol.MaxCommandBytes;
            options.ValueCountLimit = 4;
        });
        Application = builder.Build();

        Application.MapGet("ping", HandlePing);
        Application.MapGet("version", HandleVersion);
        Application.MapGet("capabilities", () => Results.Json(new
        {
            InspectionVersions = new[] { PreviewerProtocol.Version },
            MaxCommandBytes = PreviewerProtocol.MaxCommandBytes,
            MaxRequestBytes = PreviewerProtocol.MaxRequestBytes
        }));
        Application.MapPost("update/preview", HandleUpdatePreview);
            
        return Application.RunAsync($"http://localhost:{port}/");
    }

    public async Task Stop()
    {
        var application = Application;
        if (application == null)
            return;

        Application = null;
        await application.StopAsync();
        await application.DisposeAsync();
    }

    private IResult HandlePing()
    {
        return OnDocumentRefreshed == null 
            ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) 
            : Results.Ok();
    }
    
    private IResult HandleVersion()
    {
        return Results.Json(GetType().Assembly.GetName().Version);
    }
    
    internal async Task<IResult> HandleUpdatePreview(HttpRequest request)
    {
        if (request.HttpContext.Connection.RemoteIpAddress is { } address && !System.Net.IPAddress.IsLoopback(address))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.Headers.ContainsKey("Origin"))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var onDocumentRefreshed = OnDocumentRefreshed;
        if (onDocumentRefreshed == null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (!request.HasFormContentType)
            return Results.BadRequest("A multipart preview update is required.");

        IFormCollection form;
        try { form = await request.ReadFormAsync(request.HttpContext.RequestAborted); }
        catch (Exception error) when (error is InvalidDataException or BadHttpRequestException)
        { return Results.BadRequest("The multipart preview update is invalid or too large."); }
        var commandJson = form["command"].ToString();
        if (System.Text.Encoding.UTF8.GetByteCount(commandJson) > PreviewerProtocol.MaxCommandBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (string.IsNullOrWhiteSpace(commandJson))
            return Results.BadRequest("The preview command is missing.");

        DocumentSnapshot? command;
        try
        {
            command = JsonSerializer.Deserialize<DocumentSnapshot>(commandJson, JsonSerializerOptions);
        }
        catch (JsonException)
        {
            return Results.BadRequest("The preview command is invalid.");
        }

        if (command?.Pages == null)
            return Results.BadRequest("The preview pages are missing.");

        if (command.ProtocolVersion is < 0 or > PreviewerProtocol.Version ||
            (command.Inspection != null && command.ProtocolVersion != PreviewerProtocol.Version))
            return Results.BadRequest("Unsupported preview protocol; inspection requires version 1.");
        if (command.Pages.Count == 0 || command.Pages.Count > PreviewerProtocol.MaxPages ||
            command.Pages.Any(page => page == null || string.IsNullOrWhiteSpace(page.Id) || page.Id.Length > 128 ||
                !float.IsFinite(page.Width) || !float.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0) ||
            command.Pages.Select(page => page.Id).Distinct(StringComparer.Ordinal).Count() != command.Pages.Count ||
            form.Files.Count != command.Pages.Count || form.Files.Sum(file => file.Length) > PreviewerProtocol.MaxRequestBytes)
            return Results.BadRequest("Invalid preview page descriptors or size limit.");
        try
        {
            if (command.Inspection != null) PreviewerProtocol.Validate(command.Inspection, command.Pages.Count);
        }
        catch (ArgumentException error) { return Results.BadRequest(error.Message); }

        var pages = new List<PreviewPage>();
        var ownershipTransferred = false;
        try
        {
            foreach (var page in command.Pages)
            {
                request.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                var file = form.Files.GetFile(page.Id);
                if (file == null)
                    return Results.BadRequest($"Preview data for page '{page.Id}' is missing.");

                using var stream = file.OpenReadStream();
                var picture = DecodePicture(stream);
                if (picture == null)
                    return Results.BadRequest($"Preview data for page '{page.Id}' is invalid.");

                pages.Add(new PreviewPage(picture, page.Width, page.Height));
            }

            var update = new PreviewUpdate(pages, command.Inspection);
            await Dispatch(() =>
            {
                request.HttpContext.RequestAborted.ThrowIfCancellationRequested();
                onDocumentRefreshed(update);
                ownershipTransferred = true;
            });
            return Results.Ok();
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { return Results.BadRequest("Invalid preview picture data."); }
        finally
        {
            if (!ownershipTransferred)
            {
                foreach (var page in pages)
                    page.Picture.Dispose();
            }
        }
    }
}

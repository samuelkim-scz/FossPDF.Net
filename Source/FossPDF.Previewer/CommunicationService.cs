using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Avalonia.Threading;

namespace FossPDF.Previewer;

class CommunicationService
{
    public static CommunicationService Instance { get; } = new ();
    
    public event Action<ICollection<PreviewPage>>? OnDocumentRefreshed;
    public event Action<InspectionSnapshot?>? OnInspectionRefreshed;
    private readonly Func<Action, Task> dispatch;

    private WebApplication? Application { get; set; }

    private readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal CommunicationService(Func<Action, Task>? dispatch = null)
    {
        this.dispatch = dispatch ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));
    }
    
    public Task Start(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging(x => x.ClearProviders());
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.ValueLengthLimit = 16 * 1024 * 1024;
            options.MultipartBodyLengthLimit = 128 * 1024 * 1024;
        });
        builder.WebHost.UseKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024 * 1024);
        Application = builder.Build();

        Application.MapGet("ping", HandlePing);
        Application.MapGet("version", HandleVersion);
        Application.MapGet("inspection/version", () => Results.Json(InspectionSnapshot.CurrentVersion));
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
    
    private async Task<IResult> HandleUpdatePreview(HttpRequest request)
    {
        var onDocumentRefreshed = OnDocumentRefreshed;
        if (onDocumentRefreshed == null)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (!request.HasFormContentType)
            return Results.BadRequest("A multipart preview update is required.");

        IFormCollection form;
        try { form = await request.ReadFormAsync(); }
        catch (InvalidDataException) { return Results.BadRequest("The multipart preview update is invalid or too large."); }
        catch (BadHttpRequestException) { return Results.BadRequest("The preview request is invalid or too large."); }
        var commandJson = form["command"].ToString();
        if (string.IsNullOrWhiteSpace(commandJson) || commandJson.Length > 16 * 1024 * 1024)
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
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        if (command.Pages.Count > 5000 || form.Files.Count != command.Pages.Count || command.Pages.Any(page =>
                page == null || string.IsNullOrWhiteSpace(page.Id) || page.Id.Length > 256 || !identifiers.Add(page.Id) ||
                !float.IsFinite(page.Width) || !float.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0 || page.Width > 1000000 || page.Height > 1000000))
            return Results.BadRequest("Preview page IDs, count or dimensions are invalid.");
        try { command.Inspection?.Validate(command.Pages.Count); }
        catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }

        var pages = new List<PreviewPage>();
        var ownershipTransferred = false;
        try
        {
            foreach (var page in command.Pages)
            {
                var file = form.Files.GetFile(page.Id);
                if (file == null)
                    return Results.BadRequest($"Preview data for page '{page.Id}' is missing.");

                using var stream = file.OpenReadStream();
                var picture = SKPicture.Deserialize(stream);
                if (picture == null)
                    return Results.BadRequest($"Preview data for page '{page.Id}' is invalid.");

                pages.Add(new PreviewPage(picture, page.Width, page.Height));
            }

            await dispatch(() =>
            {
                ownershipTransferred = true;
                onDocumentRefreshed(pages);
                OnInspectionRefreshed?.Invoke(command.Inspection);
            });
            return Results.Ok();
        }
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

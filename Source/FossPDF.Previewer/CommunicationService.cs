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

    private WebApplication? Application { get; set; }

    private readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private CommunicationService()
    {
        
    }
    
    public Task Start(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging(x => x.ClearProviders());
        builder.WebHost.UseKestrel(options => options.Limits.MaxRequestBodySize = null);
        Application = builder.Build();

        Application.MapGet("ping", HandlePing);
        Application.MapGet("version", HandleVersion);
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

        var form = await request.ReadFormAsync();
        var commandJson = form["command"].ToString();
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

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ownershipTransferred = true;
                onDocumentRefreshed(pages);
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

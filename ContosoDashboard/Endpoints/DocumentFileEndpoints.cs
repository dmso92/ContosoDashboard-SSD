using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using ContosoDashboard.Models;
using ContosoDashboard.Services;

namespace ContosoDashboard.Endpoints;

public static class DocumentFileEndpoints
{
    public static IEndpointRouteBuilder MapDocumentFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/documents/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        }).RequireAuthorization();

        endpoints.MapPost("/api/documents/upload", async (
            HttpContext context, IAntiforgery antiforgery, IDocumentService documents,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { message = "The upload request could not be validated. Refresh the page and try again." });
            }

            var actorId = GetActorId(context.User);
            if (actorId <= 0)
                return Results.Unauthorized();
            if (!context.Request.HasFormContentType)
                return Results.BadRequest(new { message = "Submit a multipart form with one file." });

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file == null)
                return Results.BadRequest(new { message = "Choose a file to upload." });
            if (!Enum.TryParse<DocumentCategory>(form["category"], true, out var category) ||
                !Enum.IsDefined(category))
                return Results.BadRequest(new { message = "Select a valid document category." });
            int? projectId = ParseOptionalId(form["projectId"]);
            int? taskId = ParseOptionalId(form["taskId"]);
            if ((!string.IsNullOrWhiteSpace(form["projectId"]) && projectId == null) ||
                (!string.IsNullOrWhiteSpace(form["taskId"]) && taskId == null))
                return Results.BadRequest(new { message = "The selected project or task is invalid." });

            await using var stream = file.OpenReadStream();
            var result = await documents.UploadAsync(actorId, new DocumentUploadRequest
            {
                FileName = file.FileName,
                Content = stream,
                Title = form["title"].ToString(),
                Category = category,
                Description = form["description"],
                Tags = form["tags"].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                ProjectId = projectId,
                TaskId = taskId
            }, cancellationToken);
            if (result.Succeeded)
                return Results.Ok(result);
            return Results.BadRequest(result);
        }).RequireAuthorization()
          .WithMetadata(new RequestSizeLimitAttribute(DocumentService.MaximumFileBytes + 256 * 1024));

        endpoints.MapGet("/api/documents/{id:int}/download", async (
            int id, HttpContext context, IDocumentService documents, CancellationToken cancellationToken) =>
        {
            var actorId = GetActorId(context.User);
            var file = actorId <= 0 ? null : await documents.OpenFileAsync(actorId, id, false, cancellationToken);
            if (file == null)
                return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(file.Content, file.Metadata.ContentType, file.Metadata.OriginalFileName, enableRangeProcessing: true);
        }).RequireAuthorization();

        endpoints.MapGet("/api/documents/{id:int}/preview", async (
            int id, HttpContext context, IDocumentService documents, CancellationToken cancellationToken) =>
        {
            var actorId = GetActorId(context.User);
            var file = actorId <= 0 ? null : await documents.OpenFileAsync(actorId, id, true, cancellationToken);
            if (file == null)
                return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(file.Content, file.Metadata.ContentType, enableRangeProcessing: true);
        }).RequireAuthorization();

        return endpoints;
    }

    private static int GetActorId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static int? ParseOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : int.TryParse(value, out var id) && id > 0 ? id : null;
}

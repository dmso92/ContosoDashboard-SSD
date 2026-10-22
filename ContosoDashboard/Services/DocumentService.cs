using System.Text;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public sealed class DocumentUploadRequest
{
    public required string FileName { get; init; }
    public required Stream Content { get; init; }
    public required string Title { get; init; }
    public required DocumentCategory Category { get; init; }
    public string? Description { get; init; }
    public IReadOnlyCollection<string>? Tags { get; init; }
    public int? ProjectId { get; init; }
    public int? TaskId { get; init; }
}

public sealed record DocumentUploadResult(bool Succeeded, string FileName, string Message, int? DocumentId = null);

public sealed class DocumentSearch
{
    public string? Query { get; set; }
    public DocumentCategory? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string SortBy { get; set; } = "date";
    public bool Descending { get; set; } = true;
    public bool SharedWithMeOnly { get; set; }
    public bool UploadedByMeOnly { get; set; }
}

public sealed record DocumentListItem(
    int DocumentId, string Title, string? Description, DocumentCategory Category,
    string OriginalFileName, string ContentType, long FileSizeBytes, DateTime UploadedAtUtc,
    int UploaderUserId, string UploaderName, int? ProjectId, string? ProjectName,
    IReadOnlyList<string> Tags);

public sealed record DocumentFile(DocumentListItem Metadata, Stream Content);
public sealed record DocumentShareItem(int DocumentShareId, string Recipient);
public sealed record DocumentReportRow(string Label, long Count);
public sealed record DocumentReport(
    IReadOnlyList<DocumentReportRow> ByContentType,
    IReadOnlyList<DocumentReportRow> ByAction,
    IReadOnlyList<DocumentReportRow> ByUploader);

public interface IDocumentService
{
    Task<DocumentUploadResult> UploadAsync(int actorUserId, DocumentUploadRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentListItem>> SearchAsync(int actorUserId, DocumentSearch search, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentListItem>> GetRecentUploadsAsync(int actorUserId, int count = 5, CancellationToken cancellationToken = default);
    Task<int> GetVisibleCountAsync(int actorUserId, CancellationToken cancellationToken = default);
    Task<DocumentFile?> OpenFileAsync(int actorUserId, int documentId, bool preview, CancellationToken cancellationToken = default);
    Task<bool> UpdateMetadataAsync(int actorUserId, int documentId, string title, string? description, DocumentCategory category, IReadOnlyCollection<string>? tags, CancellationToken cancellationToken = default);
    Task<bool> ReplaceFileAsync(int actorUserId, int documentId, DocumentUploadRequest replacement, CancellationToken cancellationToken = default);
    Task<bool> ShareAsync(int actorUserId, int documentId, int? recipientUserId, string? recipientDepartment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentShareItem>> GetSharesAsync(int actorUserId, int documentId, CancellationToken cancellationToken = default);
    Task<bool> UnshareAsync(int actorUserId, int documentId, int shareId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int actorUserId, int documentId, bool confirmed, CancellationToken cancellationToken = default);
    Task<DocumentReport?> GetReportAsync(int actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>Document operations and access rules. The actor is always resolved from the persisted user record.</summary>
public sealed class DocumentService : IDocumentService
{
    public const long MaximumFileBytes = 25L * 1024 * 1024;
    private const int MaximumTagCount = 20;
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IFileScanner _scanner;

    public DocumentService(ApplicationDbContext db, IFileStorageService storage, IFileScanner scanner)
    {
        _db = db;
        _storage = storage;
        _scanner = scanner;
    }

    public async Task<DocumentUploadResult> UploadAsync(
        int actorUserId, DocumentUploadRequest request, CancellationToken cancellationToken = default)
    {
        var safeName = SafeFileName(request.FileName);
        string? quarantineKey = null;
        string? publishedKey = null;
        var persisted = false;
        try
        {
            var validation = ValidateMetadata(request.Title, request.Description, request.Category, request.Tags);
            if (validation != null)
                return new(false, safeName, validation);

            var actor = await _db.Users.FirstOrDefaultAsync(u => u.UserId == actorUserId, cancellationToken);
            if (actor == null)
                return new(false, safeName, "You are not authorized to upload documents.");

            var association = await ValidateAssociationAsync(actor, request.ProjectId, request.TaskId, cancellationToken);
            if (!association.Allowed)
            {
                await RecordActivityAsync(null, actorUserId, DocumentActivityAction.DeniedAccess, "association_denied", cancellationToken);
                return new(false, safeName, "You do not have access to the selected project or task.");
            }

            var buffered = await ReadLimitedAsync(request.Content, MaximumFileBytes, cancellationToken);
            if (buffered.Length == 0)
                return new(false, safeName, "The selected file is empty.");

            var detected = DetectContentType(safeName, buffered);
            if (detected == null)
                return new(false, safeName, "This file type is not supported or its contents do not match the file type.");

            await using var payload = new MemoryStream(buffered, writable: false);
            quarantineKey = await _storage.StageAsync(payload, cancellationToken);
            await using (var scanStream = await _storage.OpenQuarantineReadAsync(quarantineKey, cancellationToken))
            {
                var verdict = await _scanner.ScanAsync(scanStream, cancellationToken);
                if (verdict != FileScanVerdict.Clean)
                {
                    await _storage.DeleteQuarantineAsync(quarantineKey, cancellationToken);
                    quarantineKey = null;
                    var message = verdict == FileScanVerdict.Infected
                        ? "The file was rejected because the malware scan detected a threat."
                        : "The file could not be scanned. Upload is unavailable until the local scanner is ready.";
                    return new(false, safeName, message);
                }
            }

            publishedKey = await _storage.PublishAsync(quarantineKey, cancellationToken);
            quarantineKey = null;
            var document = new Document
            {
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                Category = request.Category,
                OriginalFileName = safeName,
                StorageKey = publishedKey,
                ContentType = detected,
                FileSizeBytes = buffered.LongLength,
                UploadedAtUtc = DateTime.UtcNow,
                UploaderUserId = actorUserId,
                ProjectId = association.ProjectId,
                TaskId = request.TaskId
            };
            foreach (var tag in NormalizeTags(request.Tags))
                document.Tags.Add(new DocumentTag { Value = tag });
            _db.Documents.Add(document);
            AddActivity(document, actorUserId, DocumentActivityAction.Upload, "success", detected);
            await _db.SaveChangesAsync(cancellationToken);
            persisted = true;
            // Notification delivery is best effort; it must not turn a committed upload into a reported failure.
            try { await NotifyProjectMembersAsync(document, actorUserId, cancellationToken); }
            catch { /* The document is safely committed; notification can be retried by a later workflow. */ }
            return new(true, safeName, "Upload complete.", document.DocumentId);
        }
        catch (DocumentInputException ex)
        {
            return new(false, safeName, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new(false, safeName, "The upload could not be completed. No document was made available.");
        }
        finally
        {
            if (quarantineKey != null)
                await _storage.DeleteQuarantineAsync(quarantineKey, CancellationToken.None);
            // The published file is compensated explicitly if database persistence failed.
            if (publishedKey != null && !persisted)
                await _storage.DeletePublishedAsync(publishedKey, CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<DocumentListItem>> SearchAsync(
        int actorUserId, DocumentSearch search, CancellationToken cancellationToken = default)
    {
        var actor = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == actorUserId, cancellationToken);
        if (actor == null)
            return Array.Empty<DocumentListItem>();

        var query = VisibleDocuments(actor).AsNoTracking();
        if (search.SharedWithMeOnly)
            query = query.Where(d => d.UploaderUserId != actorUserId && d.Shares.Any(s => s.RecipientUserId == actorUserId ||
                (s.RecipientDepartment != null && s.RecipientDepartment == actor.Department)));
        if (search.UploadedByMeOnly)
            query = query.Where(d => d.UploaderUserId == actorUserId);
        if (search.Category.HasValue)
            query = query.Where(d => d.Category == search.Category.Value);
        if (search.ProjectId.HasValue)
            query = query.Where(d => d.ProjectId == search.ProjectId.Value);
        if (search.FromUtc.HasValue)
            query = query.Where(d => d.UploadedAtUtc >= search.FromUtc.Value);
        if (search.ToUtc.HasValue)
            query = query.Where(d => d.UploadedAtUtc < search.ToUtc.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query.Trim();
            query = query.Where(d => d.Title.Contains(term) ||
                (d.Description != null && d.Description.Contains(term)) ||
                d.Tags.Any(t => t.Value.Contains(term)) ||
                d.Uploader.DisplayName.Contains(term) ||
                (d.Project != null && d.Project.Name.Contains(term)));
        }

        query = search.SortBy.ToLowerInvariant() switch
        {
            "title" => search.Descending ? query.OrderByDescending(d => d.Title) : query.OrderBy(d => d.Title),
            "category" => search.Descending ? query.OrderByDescending(d => d.Category) : query.OrderBy(d => d.Category),
            "size" or "filesize" => search.Descending ? query.OrderByDescending(d => d.FileSizeBytes) : query.OrderBy(d => d.FileSizeBytes),
            _ => search.Descending ? query.OrderByDescending(d => d.UploadedAtUtc) : query.OrderBy(d => d.UploadedAtUtc)
        };

        return await query.Take(500).Select(d => new DocumentListItem(
            d.DocumentId, d.Title, d.Description, d.Category, d.OriginalFileName, d.ContentType,
            d.FileSizeBytes, d.UploadedAtUtc, d.UploaderUserId, d.Uploader.DisplayName,
            d.ProjectId, d.Project == null ? null : d.Project.Name,
            d.Tags.Select(t => t.Value).OrderBy(t => t).ToList()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetRecentUploadsAsync(
        int actorUserId, int count = 5, CancellationToken cancellationToken = default)
    {
        var documents = await SearchAsync(actorUserId,
            new DocumentSearch { UploadedByMeOnly = true, SortBy = "date", Descending = true }, cancellationToken);
        return documents.Take(Math.Clamp(count, 1, 20)).ToList();
    }

    public async Task<int> GetVisibleCountAsync(int actorUserId, CancellationToken cancellationToken = default)
    {
        var actor = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == actorUserId, cancellationToken);
        return actor == null ? 0 : await VisibleDocuments(actor).CountAsync(cancellationToken);
    }

    public async Task<DocumentFile?> OpenFileAsync(
        int actorUserId, int documentId, bool preview, CancellationToken cancellationToken = default)
    {
        var actor = await _db.Users.FirstOrDefaultAsync(u => u.UserId == actorUserId, cancellationToken);
        var document = actor == null ? null : await VisibleDocuments(actor)
            .Include(d => d.Tags).Include(d => d.Uploader).Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
        if (document == null || (preview && !IsPreviewable(document.ContentType)))
        {
            await RecordDeniedAsync(documentId, actorUserId, cancellationToken);
            return null;
        }

        try
        {
            var stream = await _storage.OpenReadAsync(document.StorageKey, cancellationToken);
            AddActivity(document, actorUserId,
                preview ? DocumentActivityAction.Preview : DocumentActivityAction.Download, "success", document.ContentType);
            try { await _db.SaveChangesAsync(cancellationToken); }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
            return new(ToListItem(document), stream);
        }
        catch (IOException)
        {
            await RecordActivityAsync(document.DocumentId, actorUserId, DocumentActivityAction.DeniedAccess, "content_unavailable", cancellationToken);
            return null;
        }
    }

    public async Task<bool> UpdateMetadataAsync(
        int actorUserId, int documentId, string title, string? description, DocumentCategory category,
        IReadOnlyCollection<string>? tags, CancellationToken cancellationToken = default)
    {
        if (ValidateMetadata(title, description, category, tags) != null)
            return false;
        var document = await _db.Documents.Include(d => d.Tags)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
        if (document == null || !await CanManageAsync(actorUserId, document, false, cancellationToken))
        {
            await RecordDeniedAsync(documentId, actorUserId, cancellationToken);
            return false;
        }

        document.Title = title.Trim();
        document.Description = description?.Trim();
        document.Category = category;
        _db.DocumentTags.RemoveRange(document.Tags);
        document.Tags = NormalizeTags(tags).Select(tag => new DocumentTag { DocumentId = documentId, Value = tag }).ToList();
        AddActivity(document, actorUserId, DocumentActivityAction.MetadataUpdate, "success", document.ContentType);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReplaceFileAsync(
        int actorUserId, int documentId, DocumentUploadRequest replacement, CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents.Include(d => d.Tags)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
        if (document == null || !await CanManageAsync(actorUserId, document, false, cancellationToken))
        {
            await RecordDeniedAsync(documentId, actorUserId, cancellationToken);
            return false;
        }
        var validation = ValidateMetadata(replacement.Title, replacement.Description, replacement.Category, replacement.Tags);
        if (validation != null)
            return false;

        var oldKey = document.StorageKey;
        var result = await StageAndScanAsync(replacement, cancellationToken);
        if (!result.Succeeded)
            return false;

        var newKey = result.StorageKey!;
        document.StorageKey = newKey;
        document.FileSizeBytes = result.Size;
        document.OriginalFileName = SafeFileName(replacement.FileName);
        document.ContentType = result.ContentType!;
        document.Title = replacement.Title.Trim();
        document.Description = replacement.Description?.Trim();
        document.Category = replacement.Category;
        _db.DocumentTags.RemoveRange(document.Tags);
        document.Tags = NormalizeTags(replacement.Tags).Select(tag => new DocumentTag { DocumentId = documentId, Value = tag }).ToList();
        AddActivity(document, actorUserId, DocumentActivityAction.Replace, "success", document.ContentType);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            document.StorageKey = oldKey;
            await _storage.DeletePublishedAsync(newKey, CancellationToken.None);
            return false;
        }
        try { await _storage.DeletePublishedAsync(oldKey, cancellationToken); }
        catch { /* Old content is no longer addressable and can be cleaned up by local maintenance. */ }
        return true;
    }

    public async Task<bool> ShareAsync(
        int actorUserId, int documentId, int? recipientUserId, string? recipientDepartment,
        CancellationToken cancellationToken = default)
    {
        var actor = await _db.Users.FirstOrDefaultAsync(u => u.UserId == actorUserId, cancellationToken);
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
        var department = string.IsNullOrWhiteSpace(recipientDepartment) ? null : recipientDepartment.Trim();
        if (actor == null || document == null || document.UploaderUserId != actorUserId ||
            ((recipientUserId.HasValue ? 1 : 0) + (department != null ? 1 : 0) != 1))
        {
            if (document != null)
                await RecordDeniedAsync(documentId, actorUserId, cancellationToken);
            return false;
        }
        if (department?.Length > 100)
            return false;
        if (recipientUserId.HasValue &&
            !await _db.Users.AnyAsync(u => u.UserId == recipientUserId.Value, cancellationToken))
            return false;
        if (department != null && !await _db.Users.AnyAsync(u => u.Department == department, cancellationToken))
            return false;
        if (await _db.DocumentShares.AnyAsync(s => s.DocumentId == documentId &&
            (recipientUserId.HasValue ? s.RecipientUserId == recipientUserId : s.RecipientDepartment == department),
            cancellationToken))
            return false;

        var share = new DocumentShare
        {
            DocumentId = documentId,
            RecipientUserId = recipientUserId,
            RecipientDepartment = department,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.DocumentShares.Add(share);
        AddActivity(document, actorUserId, DocumentActivityAction.Share, "success", document.ContentType);
        var recipients = recipientUserId.HasValue
            ? await _db.Users.Where(u => u.UserId == recipientUserId.Value && u.InAppNotificationsEnabled)
                .Select(u => u.UserId).ToListAsync(cancellationToken)
            : await _db.Users.Where(u => u.Department == department && u.UserId != actorUserId && u.InAppNotificationsEnabled)
                .Select(u => u.UserId).ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipient,
                Title = "A document was shared with you",
                Message = $"“{document.Title}” was shared with you.",
                Type = NotificationType.DocumentShared,
                Priority = NotificationPriority.Informational,
                CreatedDate = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnshareAsync(
        int actorUserId, int documentId, int shareId, CancellationToken cancellationToken = default)
    {
        var share = await _db.DocumentShares.Include(s => s.Document)
            .FirstOrDefaultAsync(s => s.DocumentShareId == shareId && s.DocumentId == documentId, cancellationToken);
        if (share == null || share.Document.UploaderUserId != actorUserId)
        {
            await RecordDeniedAsync(documentId, actorUserId, cancellationToken);
            return false;
        }
        AddActivity(share.Document, actorUserId, DocumentActivityAction.Unshare, "success", share.Document.ContentType);
        _db.DocumentShares.Remove(share);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<DocumentShareItem>> GetSharesAsync(
        int actorUserId, int documentId, CancellationToken cancellationToken = default)
    {
        if (!await _db.Documents.AnyAsync(d => d.DocumentId == documentId && d.UploaderUserId == actorUserId, cancellationToken))
            return Array.Empty<DocumentShareItem>();
        return await _db.DocumentShares.AsNoTracking().Where(s => s.DocumentId == documentId)
            .Select(s => new DocumentShareItem(s.DocumentShareId,
                s.RecipientUserId.HasValue
                    ? s.RecipientUser!.DisplayName
                    : "Department: " + s.RecipientDepartment))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        int actorUserId, int documentId, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed)
            return false;
        var document = await _db.Documents.Include(d => d.Tags).Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
        if (document == null || !await CanManageAsync(actorUserId, document, true, cancellationToken))
        {
            await RecordDeniedAsync(documentId, actorUserId, cancellationToken);
            return false;
        }

        var key = document.StorageKey;
        _db.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = document.DocumentId,
            ActorUserId = actorUserId,
            Action = DocumentActivityAction.Delete,
            OutcomeCode = "success",
            ContentTypeAtEvent = document.ContentType,
            OccurredAtUtc = DateTime.UtcNow
        });
        _db.Documents.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);
        try { await _storage.DeletePublishedAsync(key, cancellationToken); }
        catch { /* The catalog is gone, so content is no longer reachable; cleanup can be retried locally. */ }
        return true;
    }

    public async Task<DocumentReport?> GetReportAsync(int actorUserId, CancellationToken cancellationToken = default)
    {
        if (!await _db.Users.AnyAsync(u => u.UserId == actorUserId && u.Role == UserRole.Administrator, cancellationToken))
            return null;
        var contentTypes = await _db.Documents.AsNoTracking().GroupBy(d => d.ContentType)
            .Select(g => new DocumentReportRow(g.Key, g.LongCount())).ToListAsync(cancellationToken);
        var actionCounts = await _db.DocumentActivities.AsNoTracking().GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.LongCount() }).ToListAsync(cancellationToken);
        var actions = actionCounts.Select(g => new DocumentReportRow(g.Action.ToString(), g.Count)).ToList();
        var uploaders = await _db.Documents.AsNoTracking().GroupBy(d => d.Uploader.DisplayName)
            .Select(g => new DocumentReportRow(g.Key, g.LongCount())).ToListAsync(cancellationToken);
        return new(contentTypes, actions, uploaders);
    }

    private IQueryable<Document> VisibleDocuments(User actor)
    {
        var userId = actor.UserId;
        var isAdmin = actor.Role == UserRole.Administrator;
        var isTeamLead = actor.Role == UserRole.TeamLead && !string.IsNullOrWhiteSpace(actor.Department);
        var department = actor.Department;
        return _db.Documents.Where(d =>
            d.UploaderUserId == userId || isAdmin ||
            (d.ProjectId.HasValue && _db.Projects.Any(p => p.ProjectId == d.ProjectId &&
                (p.ProjectManagerId == userId || p.ProjectMembers.Any(pm => pm.UserId == userId)))) ||
            (d.TaskId.HasValue && _db.Tasks.Any(t => t.TaskId == d.TaskId &&
                (t.AssignedUserId == userId || t.CreatedByUserId == userId))) ||
            (isTeamLead && _db.Users.Any(u => u.UserId == d.UploaderUserId && u.Department == department)) ||
            d.Shares.Any(s => s.RecipientUserId == userId ||
                (department != null && s.RecipientDepartment == department)));
    }

    private async Task<bool> CanManageAsync(int actorUserId, Document document, bool delete, CancellationToken cancellationToken)
    {
        var actor = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == actorUserId, cancellationToken);
        if (actor == null)
            return false;
        if (actor.Role == UserRole.Administrator || actorUserId == document.UploaderUserId)
            return true;
        if (actor.Role == UserRole.ProjectManager && document.ProjectId.HasValue &&
            await _db.Projects.AnyAsync(p => p.ProjectId == document.ProjectId && p.ProjectManagerId == actorUserId, cancellationToken))
            return true;
        if (!delete && actor.Role == UserRole.TeamLead && !string.IsNullOrWhiteSpace(actor.Department))
            return await _db.Users.AnyAsync(u => u.UserId == document.UploaderUserId && u.Department == actor.Department, cancellationToken);
        return false;
    }

    private async Task<(bool Allowed, int? ProjectId)> ValidateAssociationAsync(
        User actor, int? projectId, int? taskId, CancellationToken cancellationToken)
    {
        int? authoritativeProjectId = projectId;
        if (taskId.HasValue)
        {
            var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId.Value, cancellationToken);
            if (task == null)
                return (false, null);
            var taskAuthorized = task.AssignedUserId == actor.UserId || task.CreatedByUserId == actor.UserId ||
                await CanAccessProjectAsync(actor.UserId, task.ProjectId, cancellationToken);
            if (!taskAuthorized || (projectId.HasValue && projectId != task.ProjectId))
                return (false, null);
            authoritativeProjectId = task.ProjectId;
        }
        if (authoritativeProjectId.HasValue &&
            !await CanAccessProjectAsync(actor.UserId, authoritativeProjectId, cancellationToken))
            return (false, null);
        return (true, authoritativeProjectId);
    }

    private async Task<bool> CanAccessProjectAsync(int userId, int? projectId, CancellationToken cancellationToken)
    {
        if (!projectId.HasValue)
            return false;
        if (await _db.Users.AnyAsync(u => u.UserId == userId && u.Role == UserRole.Administrator, cancellationToken))
            return true;
        return await _db.Projects.AnyAsync(p => p.ProjectId == projectId &&
            (p.ProjectManagerId == userId || p.ProjectMembers.Any(pm => pm.UserId == userId)), cancellationToken);
    }

    private async Task NotifyProjectMembersAsync(Document document, int actorUserId, CancellationToken cancellationToken)
    {
        if (!document.ProjectId.HasValue)
            return;
        var recipients = await _db.ProjectMembers.Where(pm => pm.ProjectId == document.ProjectId && pm.UserId != actorUserId)
            .Select(pm => pm.UserId).Distinct().ToListAsync(cancellationToken);
        var managerId = await _db.Projects.Where(p => p.ProjectId == document.ProjectId)
            .Select(p => p.ProjectManagerId).FirstOrDefaultAsync(cancellationToken);
        if (managerId != 0 && managerId != actorUserId)
            recipients.Add(managerId);
        recipients = await _db.Users.Where(u => recipients.Contains(u.UserId) && u.InAppNotificationsEnabled)
            .Select(u => u.UserId).ToListAsync(cancellationToken);
        foreach (var recipient in recipients.Distinct())
            _db.Notifications.Add(new Notification
            {
                UserId = recipient,
                Title = "A project document was added",
                Message = $"“{document.Title}” was added to a project you can access.",
                Type = NotificationType.ProjectDocumentAdded,
                Priority = NotificationPriority.Informational,
                CreatedDate = DateTime.UtcNow
            });
        if (recipients.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(bool Succeeded, string? StorageKey, long Size, string? ContentType)> StageAndScanAsync(
        DocumentUploadRequest request, CancellationToken cancellationToken)
    {
        var buffered = await ReadLimitedAsync(request.Content, MaximumFileBytes, cancellationToken);
        var contentType = DetectContentType(SafeFileName(request.FileName), buffered);
        if (buffered.Length == 0 || contentType == null)
            return (false, null, 0, null);
        await using var payload = new MemoryStream(buffered, writable: false);
        var quarantine = await _storage.StageAsync(payload, cancellationToken);
        try
        {
            await using var scanStream = await _storage.OpenQuarantineReadAsync(quarantine, cancellationToken);
            if (await _scanner.ScanAsync(scanStream, cancellationToken) != FileScanVerdict.Clean)
                return (false, null, 0, null);
            var key = await _storage.PublishAsync(quarantine, cancellationToken);
            return (true, key, buffered.LongLength, contentType);
        }
        finally
        {
            await _storage.DeleteQuarantineAsync(quarantine, CancellationToken.None);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream input, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
                throw new DocumentInputException("Each file must be no larger than 25 MiB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static string? ValidateMetadata(string title, string? description, DocumentCategory category, IReadOnlyCollection<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 255)
            return "Enter a title of 1 to 255 characters.";
        if (description?.Length > 2000)
            return "Description must be 2,000 characters or fewer.";
        if (!Enum.IsDefined(category))
            return "Select a valid document category.";
        if (tags?.Count > MaximumTagCount || tags?.Any(t => string.IsNullOrWhiteSpace(t) || t.Trim().Length > 64) == true)
            return "Use up to 20 tags, each no longer than 64 characters.";
        return null;
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyCollection<string>? tags) =>
        (tags ?? Array.Empty<string>()).Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumTagCount).ToList();

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";
        return name.Length <= 255 ? name : name[^255..];
    }

    private static string? DetectContentType(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var header = content.AsSpan(0, Math.Min(content.Length, 16));
        var pdf = header.StartsWith("%PDF-"u8);
        var png = header.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var jpg = header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
        var zip = header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4b && header[2] == 0x03 && header[3] == 0x04;
        var ole = header.StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 });
        return extension switch
        {
            ".pdf" when pdf => "application/pdf",
            ".png" when png => "image/png",
            ".jpg" or ".jpeg" when jpg => "image/jpeg",
            ".docx" when zip && ContainsOfficeEntry(content, "word/document.xml") => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" when zip && ContainsOfficeEntry(content, "xl/workbook.xml") => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" when zip && ContainsOfficeEntry(content, "ppt/presentation.xml") => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".doc" when ole => "application/msword",
            ".xls" when ole => "application/vnd.ms-excel",
            ".ppt" when ole => "application/vnd.ms-powerpoint",
            ".txt" when IsText(content) => "text/plain",
            _ => null
        };
    }

    private static bool IsText(byte[] content)
    {
        if (content.Any(b => b == 0))
            return false;
        try { _ = new UTF8Encoding(false, true).GetString(content); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool ContainsOfficeEntry(byte[] content, string requiredEntry)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            return archive.GetEntry("[Content_Types].xml") != null && archive.GetEntry(requiredEntry) != null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsPreviewable(string contentType) =>
        contentType == "application/pdf" || contentType == "image/png" || contentType == "image/jpeg";

    private DocumentListItem ToListItem(Document d) =>
        new(d.DocumentId, d.Title, d.Description, d.Category, d.OriginalFileName, d.ContentType,
            d.FileSizeBytes, d.UploadedAtUtc, d.UploaderUserId, d.Uploader.DisplayName,
            d.ProjectId, d.Project?.Name, d.Tags.Select(t => t.Value).ToList());

    private void AddActivity(Document document, int actorUserId, DocumentActivityAction action, string outcome, string? contentType) =>
        _db.DocumentActivities.Add(new DocumentActivity
        {
            Document = document,
            ActorUserId = actorUserId,
            Action = action,
            OutcomeCode = outcome,
            ContentTypeAtEvent = contentType,
            OccurredAtUtc = DateTime.UtcNow
        });

    private async Task RecordDeniedAsync(int documentId, int actorUserId, CancellationToken cancellationToken)
    {
        var document = await _db.Documents.AsNoTracking().Where(d => d.DocumentId == documentId)
            .Select(d => new { d.DocumentId, d.ContentType }).FirstOrDefaultAsync(cancellationToken);
        var knownActor = await _db.Users.AnyAsync(u => u.UserId == actorUserId, cancellationToken) ? actorUserId : (int?)null;
        await RecordActivityAsync(document?.DocumentId, knownActor, DocumentActivityAction.DeniedAccess,
            "not_available", cancellationToken, document?.ContentType);
    }

    private async Task RecordActivityAsync(
        int? documentId, int? actorUserId, DocumentActivityAction action, string outcome,
        CancellationToken cancellationToken, string? contentType = null)
    {
        _db.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = documentId,
            ActorUserId = actorUserId,
            Action = action,
            OutcomeCode = outcome,
            ContentTypeAtEvent = contentType,
            OccurredAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed class DocumentInputException(string message) : Exception(message)
    {
    }
}

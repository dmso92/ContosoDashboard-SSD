using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class Document
{
    public int DocumentId { get; set; }

    [Required, MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public DocumentCategory Category { get; set; }

    [Required, MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string StorageKey { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public int UploaderUserId { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }

    public virtual User Uploader { get; set; } = null!;
    public virtual Project? Project { get; set; }
    public virtual TaskItem? Task { get; set; }
    public virtual ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<DocumentActivity> Activities { get; set; } = new List<DocumentActivity>();
}

public enum DocumentCategory
{
    ProjectDocuments,
    TeamResources,
    PersonalFiles,
    Reports,
    Presentations,
    Other
}

public class DocumentTag
{
    public int DocumentTagId { get; set; }
    public int DocumentId { get; set; }

    [Required, MaxLength(64)]
    public string Value { get; set; } = string.Empty;

    public virtual Document Document { get; set; } = null!;
}

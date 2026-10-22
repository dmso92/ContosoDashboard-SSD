using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    public int DocumentActivityId { get; set; }
    public int? DocumentId { get; set; }
    public int? ActorUserId { get; set; }
    public DocumentActivityAction Action { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? OutcomeCode { get; set; }

    [MaxLength(255)]
    public string? ContentTypeAtEvent { get; set; }

    public virtual Document? Document { get; set; }
    public virtual User? Actor { get; set; }
}

public enum DocumentActivityAction
{
    Upload,
    Download,
    Preview,
    MetadataUpdate,
    Replace,
    Delete,
    Share,
    Unshare,
    DeniedAccess
}

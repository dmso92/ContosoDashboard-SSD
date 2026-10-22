using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    public int DocumentShareId { get; set; }
    public int DocumentId { get; set; }
    public int? RecipientUserId { get; set; }

    [MaxLength(100)]
    public string? RecipientDepartment { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual Document Document { get; set; } = null!;
    public virtual User? RecipientUser { get; set; }
    public virtual User CreatedBy { get; set; } = null!;
}

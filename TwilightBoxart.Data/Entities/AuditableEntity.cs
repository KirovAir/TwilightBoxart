namespace TwilightBoxart.Data.Entities;

public abstract class AuditableEntity
{
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
}

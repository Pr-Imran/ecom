namespace FashionStore.Domain.Entities;

public abstract class AuditedEntity : Entity
{
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }

    protected AuditedEntity() : base() { }
    protected AuditedEntity(Guid id) : base(id) { }
}

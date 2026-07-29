namespace FashionStore.Domain.Entities;

public abstract class SoftDeletableEntity : AuditedEntity
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    protected SoftDeletableEntity() : base() { }
    protected SoftDeletableEntity(Guid id) : base(id) { }
}

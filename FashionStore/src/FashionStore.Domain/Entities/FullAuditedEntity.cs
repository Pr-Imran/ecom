namespace FashionStore.Domain.Entities;

public abstract class FullAuditedEntity : SoftDeletableEntity
{
    protected FullAuditedEntity() : base() { }
    protected FullAuditedEntity(Guid id) : base(id) { }
}

namespace FashionStore.Domain.Entities;

public class Product : AuditedEntity
{
    public Guid? BrandId { get; set; }
    public Guid? CollectionId { get; set; }
    public Guid? CategoryId { get; set; }
}

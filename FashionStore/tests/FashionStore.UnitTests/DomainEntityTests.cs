namespace FashionStore.UnitTests;

public class DomainEntityTests
{
    [Fact]
    public void Entity_Equals_SameId_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var entity1 = new TestEntity(id);
        var entity2 = new TestEntity(id);

        Assert.True(entity1 == entity2);
        Assert.True(entity1.Equals(entity2));
    }

    [Fact]
    public void Entity_Equals_DifferentId_ReturnsFalse()
    {
        var entity1 = new TestEntity(Guid.NewGuid());
        var entity2 = new TestEntity(Guid.NewGuid());

        Assert.False(entity1 == entity2);
    }

    [Fact]
    public void Entity_NewInstance_HasNonEmptyId()
    {
        var entity = new TestEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
    }

    private sealed class TestEntity : Domain.Entities.Entity
    {
        public TestEntity() : base() { }
        public TestEntity(Guid id) : base(id) { }
    }
}

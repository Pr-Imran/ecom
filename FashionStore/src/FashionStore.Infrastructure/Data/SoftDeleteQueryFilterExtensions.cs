using Microsoft.EntityFrameworkCore;

namespace FashionStore.Infrastructure.Data;

public static class SoftDeleteQueryFilterExtensions
{
    public static void AddSoftDeleteQueryFilter(this Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType)
    {
        var methodToCall = typeof(SoftDeleteQueryFilterExtensions)
            .GetMethod(nameof(GetSoftDeleteFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.MakeGenericMethod(entityType.ClrType);

        var filter = methodToCall?.Invoke(null, Array.Empty<object>());
        entityType.SetQueryFilter((System.Linq.Expressions.LambdaExpression?)filter);
    }

    private static System.Linq.Expressions.Expression<Func<TEntity, bool>> GetSoftDeleteFilter<TEntity>()
        where TEntity : FashionStore.Domain.Entities.SoftDeletableEntity
    {
        return e => !e.IsDeleted;
    }
}

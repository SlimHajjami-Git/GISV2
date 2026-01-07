using GisAPI.Domain.Common;

namespace GisAPI.Domain.Interfaces;

public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
}

public interface ITenantRepository<T> : IRepository<T> where T : TenantEntity
{
    Task<IReadOnlyList<T>> GetAllForTenantAsync(CancellationToken ct = default);
}

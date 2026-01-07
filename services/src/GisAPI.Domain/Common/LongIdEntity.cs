namespace GisAPI.Domain.Common;

/// <summary>
/// Base class for entities that require a long (bigint) primary key
/// Used for high-volume tables like GPS positions, fuel records, vehicle stops
/// </summary>
public abstract class LongIdEntity
{
    public long Id { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not LongIdEntity other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (GetType() != other.GetType())
            return false;

        if (Id == default || other.Id == default)
            return false;

        return Id == other.Id;
    }

    public override int GetHashCode() => (GetType().ToString() + Id).GetHashCode();

    public static bool operator ==(LongIdEntity? a, LongIdEntity? b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.Equals(b);
    }

    public static bool operator !=(LongIdEntity? a, LongIdEntity? b) => !(a == b);
}

/// <summary>
/// Auditable entity with long ID for high-volume tables
/// </summary>
public abstract class LongIdAuditableEntity : LongIdEntity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tenant entity with long ID for high-volume multi-tenant tables
/// </summary>
public abstract class LongIdTenantEntity : LongIdAuditableEntity, ITenantEntity
{
    public int CompanyId { get; set; }
}

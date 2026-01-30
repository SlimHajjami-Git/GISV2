namespace GisAPI.Domain.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}



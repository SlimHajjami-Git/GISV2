namespace GisAPI.Application.Common.Interfaces;

public interface IRequiresPermissions
{
    IReadOnlyCollection<string> RequiredPermissions { get; }
}

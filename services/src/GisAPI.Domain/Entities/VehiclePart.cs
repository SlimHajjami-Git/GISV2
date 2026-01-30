namespace GisAPI.Domain.Entities;

public class VehiclePart
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public PartCategory Category { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PartNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}



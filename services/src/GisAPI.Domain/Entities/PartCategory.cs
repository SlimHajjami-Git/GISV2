namespace GisAPI.Domain.Entities;

public class PartCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<VehiclePart> Parts { get; set; } = new List<VehiclePart>();
}



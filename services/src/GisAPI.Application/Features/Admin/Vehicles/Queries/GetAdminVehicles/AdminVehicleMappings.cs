using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;

internal static class AdminVehicleMappings
{
    public static AdminVehicleDto ToAdminVehicleDto(this Vehicle vehicle)
    {
        return new AdminVehicleDto
        {
            Id = vehicle.Id,
            Name = vehicle.Name,
            Type = vehicle.Type,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Plate = vehicle.Plate,
            Year = vehicle.Year,
            Color = vehicle.Color,
            Status = vehicle.Status,
            HasGps = vehicle.HasGps,
            Mileage = vehicle.Mileage,
            CompanyId = vehicle.CompanyId,
            CompanyName = vehicle.Societe?.Name,
            GpsDeviceId = vehicle.GpsDeviceId,
            GpsImei = vehicle.GpsDevice?.DeviceUid,
            GpsMat = vehicle.GpsDevice?.Mat,
            GpsModel = vehicle.GpsDevice?.Model,
            GpsFirmwareVersion = vehicle.GpsDevice?.FirmwareVersion,
            AssignedDriverId = vehicle.AssignedDriverId,
            AssignedDriverName = vehicle.AssignedDriver?.Name,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt
        };
    }
}

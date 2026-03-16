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
            FuelType = vehicle.FuelType,
            FuelTankCapacity = vehicle.FuelTankCapacity,
            GpsImei = vehicle.GpsDevice?.DeviceUid,
            GpsMat = vehicle.GpsDevice?.Mat,
            GpsBrand = vehicle.GpsDevice?.Brand,
            GpsModel = vehicle.GpsDevice?.Model,
            GpsFirmwareVersion = vehicle.GpsDevice?.FirmwareVersion,
            GpsFuelSensorMode = vehicle.GpsDevice?.FuelSensorMode,
            GpsSimNumber = vehicle.GpsDevice?.SimNumber,
            GpsSimOperator = vehicle.GpsDevice?.SimOperator,
            GpsInstallationDate = vehicle.GpsDevice?.InstallationDate,
            AssignedDriverId = vehicle.AssignedDriverId,
            AssignedDriverName = vehicle.AssignedDriver?.FullName,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt
        };
    }
}




using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public class ImportFuelPricesCommandHandler : IRequestHandler<ImportFuelPricesCommand, ImportResultDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public ImportFuelPricesCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<ImportResultDto> Handle(ImportFuelPricesCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var fuelTypes = await _context.FuelTypes.ToListAsync(cancellationToken);
        var fuelTypeMap = fuelTypes.ToDictionary(ft => ft.Code.ToUpper(), ft => ft.Id);

        var errors = new List<string>();
        var successCount = 0;
        var rowNumber = 0;

        foreach (var row in request.Rows)
        {
            rowNumber++;
            try
            {
                // Validate fuel type
                var fuelTypeCode = row.FuelTypeCode.ToUpper().Trim();
                if (!fuelTypeMap.TryGetValue(fuelTypeCode, out var fuelTypeId))
                {
                    errors.Add($"Row {rowNumber}: Invalid fuel type code '{row.FuelTypeCode}'");
                    continue;
                }

                // Validate price
                if (row.PricePerLiter <= 0)
                {
                    errors.Add($"Row {rowNumber}: Price must be greater than 0");
                    continue;
                }

                var effectiveFrom = row.EffectiveFrom.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(row.EffectiveFrom, DateTimeKind.Utc)
                    : row.EffectiveFrom.ToUniversalTime();

                DateTime? effectiveTo = null;
                if (row.EffectiveTo.HasValue)
                {
                    effectiveTo = row.EffectiveTo.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(row.EffectiveTo.Value, DateTimeKind.Utc)
                        : row.EffectiveTo.Value.ToUniversalTime();
                }

                var fuelPrice = new FuelPricing
                {
                    CompanyId = companyId,
                    FuelTypeId = fuelTypeId,
                    PricePerLiter = row.PricePerLiter,
                    EffectiveFrom = effectiveFrom,
                    EffectiveTo = effectiveTo,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.FuelPricings.Add(fuelPrice);
                successCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        if (successCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new ImportResultDto(
            request.Rows.Count,
            successCount,
            request.Rows.Count - successCount,
            errors
        );
    }
}

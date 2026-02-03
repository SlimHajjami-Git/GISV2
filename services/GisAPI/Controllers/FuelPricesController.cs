using GisAPI.Application.Features.FuelPrices;
using GisAPI.Application.Features.FuelPrices.Commands;
using GisAPI.Application.Features.FuelPrices.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FuelPricesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<FuelPricesController> _logger;

    public FuelPricesController(IMediator mediator, ILogger<FuelPricesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Get all fuel prices with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<FuelPriceDto>>> GetFuelPrices(
        [FromQuery] int? fuelTypeId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetFuelPricesQuery(fuelTypeId, isActive, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get current active fuel prices for all fuel types
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<List<FuelPriceDto>>> GetCurrentFuelPrices()
    {
        var result = await _mediator.Send(new GetCurrentFuelPricesQuery());
        return Ok(result);
    }

    /// <summary>
    /// Get all fuel types
    /// </summary>
    [HttpGet("types")]
    public async Task<ActionResult<List<FuelTypeDto>>> GetFuelTypes()
    {
        var result = await _mediator.Send(new GetFuelTypesQuery());
        return Ok(result);
    }

    /// <summary>
    /// Create a new fuel price
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<int>> CreateFuelPrice([FromBody] CreateFuelPriceRequest request)
    {
        var command = new CreateFuelPriceCommand(
            request.FuelTypeId,
            request.PricePerLiter,
            request.EffectiveFrom,
            request.EffectiveTo
        );

        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetFuelPrices), new { id }, new { Id = id });
    }

    /// <summary>
    /// Update an existing fuel price
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateFuelPrice(int id, [FromBody] UpdateFuelPriceRequest request)
    {
        var command = new UpdateFuelPriceCommand(
            id,
            request.FuelTypeId,
            request.PricePerLiter,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsActive
        );

        var success = await _mediator.Send(command);
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Delete a fuel price
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteFuelPrice(int id)
    {
        var success = await _mediator.Send(new DeleteFuelPriceCommand(id));
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Import fuel prices from Excel file
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> ImportFuelPrices(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".xlsx" && extension != ".xls")
            return BadRequest("Invalid file format. Please upload an Excel file (.xlsx or .xls)");

        try
        {
            var rows = new List<FuelPriceImportRow>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;

                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheet(1);
                var rowCount = worksheet.LastRowUsed()?.RowNumber() ?? 0;

                // Skip header row, start from row 2
                for (int row = 2; row <= rowCount; row++)
                {
                    var fuelTypeCode = worksheet.Cell(row, 1).GetString()?.Trim();
                    var priceStr = worksheet.Cell(row, 2).GetString()?.Trim();
                    var effectiveFromStr = worksheet.Cell(row, 3).GetString()?.Trim();
                    var effectiveToStr = worksheet.Cell(row, 4).GetString()?.Trim();

                    if (string.IsNullOrEmpty(fuelTypeCode) || string.IsNullOrEmpty(priceStr))
                        continue;

                    if (!decimal.TryParse(priceStr, out var price))
                    {
                        // Try to get as number directly from cell
                        try { price = (decimal)worksheet.Cell(row, 2).GetDouble(); }
                        catch { continue; }
                    }

                    DateTime effectiveFrom;
                    if (!DateTime.TryParse(effectiveFromStr, out effectiveFrom))
                    {
                        try { effectiveFrom = worksheet.Cell(row, 3).GetDateTime(); }
                        catch { effectiveFrom = DateTime.UtcNow; }
                    }

                    DateTime? effectiveTo = null;
                    if (!string.IsNullOrEmpty(effectiveToStr))
                    {
                        if (DateTime.TryParse(effectiveToStr, out var endDate))
                            effectiveTo = endDate;
                        else
                        {
                            try { effectiveTo = worksheet.Cell(row, 4).GetDateTime(); }
                            catch { }
                        }
                    }

                    rows.Add(new FuelPriceImportRow(fuelTypeCode, price, effectiveFrom, effectiveTo));
                }
            }

            if (rows.Count == 0)
                return BadRequest("No valid data found in the Excel file");

            var result = await _mediator.Send(new ImportFuelPricesCommand(rows));
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing fuel prices from Excel file");
            return BadRequest($"Error processing file: {ex.Message}");
        }
    }

    /// <summary>
    /// Download Excel template for fuel price import
    /// </summary>
    [HttpGet("import/template")]
    public async Task<IActionResult> DownloadTemplate()
    {
        var fuelTypes = await _mediator.Send(new GetFuelTypesQuery());

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Fuel Prices");

        // Headers
        worksheet.Cell(1, 1).Value = "FuelTypeCode";
        worksheet.Cell(1, 2).Value = "PricePerLiter";
        worksheet.Cell(1, 3).Value = "EffectiveFrom";
        worksheet.Cell(1, 4).Value = "EffectiveTo (Optional)";

        // Style headers
        var headerRange = worksheet.Range(1, 1, 1, 4);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Add example rows with actual fuel type codes
        var row = 2;
        foreach (var fuelType in fuelTypes.Take(3))
        {
            worksheet.Cell(row, 1).Value = fuelType.Code;
            worksheet.Cell(row, 2).Value = 12.50;
            worksheet.Cell(row, 3).Value = DateTime.UtcNow.ToString("yyyy-MM-dd");
            worksheet.Cell(row, 4).Value = "";
            row++;
        }

        // Add instruction sheet
        var instructionSheet = workbook.Worksheets.Add("Instructions");
        instructionSheet.Cell(1, 1).Value = "Instructions for Fuel Price Import";
        instructionSheet.Cell(1, 1).Style.Font.Bold = true;
        instructionSheet.Cell(3, 1).Value = "Column Descriptions:";
        instructionSheet.Cell(4, 1).Value = "FuelTypeCode: Code of the fuel type (see Available Fuel Types below)";
        instructionSheet.Cell(5, 1).Value = "PricePerLiter: Price per liter (decimal number)";
        instructionSheet.Cell(6, 1).Value = "EffectiveFrom: Date when price becomes effective (YYYY-MM-DD)";
        instructionSheet.Cell(7, 1).Value = "EffectiveTo: Optional end date for the price (YYYY-MM-DD)";

        instructionSheet.Cell(9, 1).Value = "Available Fuel Types:";
        instructionSheet.Cell(9, 1).Style.Font.Bold = true;
        var instructRow = 10;
        foreach (var fuelType in fuelTypes)
        {
            instructionSheet.Cell(instructRow, 1).Value = $"{fuelType.Code} - {fuelType.Name}";
            instructRow++;
        }

        worksheet.Columns().AdjustToContents();
        instructionSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "fuel_prices_template.xlsx"
        );
    }
}

// Request DTOs
public record CreateFuelPriceRequest(
    int FuelTypeId,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo
);

public record UpdateFuelPriceRequest(
    int FuelTypeId,
    decimal PricePerLiter,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive
);

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrandsController : ControllerBase
{
    private readonly IGisDbContext _context;

    public BrandsController(IGisDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<BrandDto>>> GetBrands()
    {
        var brands = await _context.Brands
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.LogoUrl, b.Models.Count(m => m.IsActive)))
            .ToListAsync();

        return Ok(brands);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BrandDetailDto>> GetBrand(int id)
    {
        var brand = await _context.Brands
            .Where(b => b.Id == id)
            .Select(b => new BrandDetailDto(
                b.Id,
                b.Name,
                b.LogoUrl,
                b.IsActive,
                b.Models.Where(m => m.IsActive).Select(m => new VehicleModelDto(m.Id, m.Name, m.VehicleType)).ToList()
            ))
            .FirstOrDefaultAsync();

        if (brand == null)
            return NotFound();

        return Ok(brand);
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateBrand([FromBody] CreateBrandRequest request)
    {
        var brand = new Brand
        {
            Name = request.Name,
            LogoUrl = request.LogoUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Brands.Add(brand);
        await _context.SaveChangesAsync(default);

        return CreatedAtAction(nameof(GetBrand), new { id = brand.Id }, brand.Id);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateBrand(int id, [FromBody] UpdateBrandRequest request)
    {
        var brand = await _context.Brands.FindAsync(id);
        if (brand == null)
            return NotFound();

        brand.Name = request.Name;
        brand.LogoUrl = request.LogoUrl;
        brand.IsActive = request.IsActive;

        await _context.SaveChangesAsync(default);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteBrand(int id)
    {
        var brand = await _context.Brands.FindAsync(id);
        if (brand == null)
            return NotFound();

        brand.IsActive = false;
        await _context.SaveChangesAsync(default);
        return NoContent();
    }

    [HttpGet("{brandId}/models")]
    public async Task<ActionResult<List<VehicleModelDto>>> GetModelsByBrand(int brandId)
    {
        var models = await _context.VehicleModels
            .Where(m => m.BrandId == brandId && m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new VehicleModelDto(m.Id, m.Name, m.VehicleType))
            .ToListAsync();

        return Ok(models);
    }

    [HttpPost("{brandId}/models")]
    public async Task<ActionResult<int>> CreateModel(int brandId, [FromBody] CreateModelRequest request)
    {
        var brand = await _context.Brands.FindAsync(brandId);
        if (brand == null)
            return NotFound("Brand not found");

        var model = new VehicleModel
        {
            BrandId = brandId,
            Name = request.Name,
            VehicleType = request.VehicleType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.VehicleModels.Add(model);
        await _context.SaveChangesAsync(default);

        return CreatedAtAction(nameof(GetModelsByBrand), new { brandId }, model.Id);
    }

    [HttpPut("models/{id}")]
    public async Task<ActionResult> UpdateModel(int id, [FromBody] UpdateModelRequest request)
    {
        var model = await _context.VehicleModels.FindAsync(id);
        if (model == null)
            return NotFound();

        model.Name = request.Name;
        model.VehicleType = request.VehicleType;
        model.IsActive = request.IsActive;

        await _context.SaveChangesAsync(default);
        return NoContent();
    }

    [HttpDelete("models/{id}")]
    public async Task<ActionResult> DeleteModel(int id)
    {
        var model = await _context.VehicleModels.FindAsync(id);
        if (model == null)
            return NotFound();

        model.IsActive = false;
        await _context.SaveChangesAsync(default);
        return NoContent();
    }
}

public record BrandDto(int Id, string Name, string? LogoUrl, int ModelCount);
public record BrandDetailDto(int Id, string Name, string? LogoUrl, bool IsActive, List<VehicleModelDto> Models);
public record VehicleModelDto(int Id, string Name, string? VehicleType);
public record CreateBrandRequest(string Name, string? LogoUrl);
public record UpdateBrandRequest(string Name, string? LogoUrl, bool IsActive);
public record CreateModelRequest(string Name, string? VehicleType);
public record UpdateModelRequest(string Name, string? VehicleType, bool IsActive);





using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Controllers;

/// <summary>
/// Lecture seule du référentiel marques / modèles, ouverte à tout utilisateur connecté
/// (formulaire véhicule client et admin). Les créations, renommages et désactivations
/// sont dans AdminBrandsController (/api/admin/brands) : exposées ici, elles laissaient
/// n'importe quel client modifier ces données globales pour toute la plateforme.
/// </summary>
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
}

public record BrandDto(int Id, string Name, string? LogoUrl, int ModelCount);
public record BrandDetailDto(int Id, string Name, string? LogoUrl, bool IsActive, List<VehicleModelDto> Models);
public record VehicleModelDto(int Id, string Name, string? VehicleType);

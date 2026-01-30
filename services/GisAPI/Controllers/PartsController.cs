using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PartsController : ControllerBase
{
    private readonly IGisDbContext _context;

    public PartsController(IGisDbContext context)
    {
        _context = context;
    }

    // ============ CATEGORIES ============

    [HttpGet("categories")]
    public async Task<ActionResult<List<PartCategoryDto>>> GetCategories()
    {
        var categories = await _context.PartCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new PartCategoryDto(
                c.Id, 
                c.Name, 
                c.Description, 
                c.Icon,
                c.Parts.Count(p => p.IsActive)
            ))
            .ToListAsync();

        return Ok(categories);
    }

    [HttpGet("categories/{id}")]
    public async Task<ActionResult<PartCategoryDto>> GetCategory(int id)
    {
        var category = await _context.PartCategories
            .Where(c => c.Id == id)
            .Select(c => new PartCategoryDto(
                c.Id, 
                c.Name, 
                c.Description, 
                c.Icon,
                c.Parts.Count(p => p.IsActive)
            ))
            .FirstOrDefaultAsync();

        if (category == null)
            return NotFound();

        return Ok(category);
    }

    [HttpPost("categories")]
    public async Task<ActionResult<PartCategoryDto>> CreateCategory(CreateCategoryRequest request)
    {
        var category = new PartCategory
        {
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.PartCategories.Add(category);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, 
            new PartCategoryDto(category.Id, category.Name, category.Description, category.Icon, 0));
    }

    [HttpPut("categories/{id}")]
    public async Task<ActionResult> UpdateCategory(int id, UpdateCategoryRequest request)
    {
        var category = await _context.PartCategories.FindAsync(id);
        if (category == null)
            return NotFound();

        category.Name = request.Name;
        category.Description = request.Description;
        category.Icon = request.Icon;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("categories/{id}")]
    public async Task<ActionResult> DeleteCategory(int id)
    {
        var category = await _context.PartCategories.FindAsync(id);
        if (category == null)
            return NotFound();

        category.IsActive = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ============ PARTS ============

    [HttpGet("categories/{categoryId}/parts")]
    public async Task<ActionResult<List<VehiclePartDto>>> GetPartsByCategory(int categoryId)
    {
        var parts = await _context.VehicleParts
            .Where(p => p.CategoryId == categoryId && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new VehiclePartDto(p.Id, p.CategoryId, p.Name, p.Description, p.PartNumber))
            .ToListAsync();

        return Ok(parts);
    }

    [HttpGet("parts")]
    public async Task<ActionResult<List<VehiclePartWithCategoryDto>>> GetAllParts()
    {
        var parts = await _context.VehicleParts
            .Where(p => p.IsActive)
            .Include(p => p.Category)
            .OrderBy(p => p.Category.Name)
            .ThenBy(p => p.Name)
            .Select(p => new VehiclePartWithCategoryDto(
                p.Id, 
                p.CategoryId, 
                p.Category.Name,
                p.Name, 
                p.Description, 
                p.PartNumber
            ))
            .ToListAsync();

        return Ok(parts);
    }

    [HttpPost("parts")]
    public async Task<ActionResult<VehiclePartDto>> CreatePart(CreatePartRequest request)
    {
        var category = await _context.PartCategories.FindAsync(request.CategoryId);
        if (category == null)
            return BadRequest("Category not found");

        var part = new VehiclePart
        {
            CategoryId = request.CategoryId,
            Name = request.Name,
            Description = request.Description,
            PartNumber = request.PartNumber,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.VehicleParts.Add(part);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPartsByCategory), new { categoryId = part.CategoryId }, 
            new VehiclePartDto(part.Id, part.CategoryId, part.Name, part.Description, part.PartNumber));
    }

    [HttpPut("parts/{id}")]
    public async Task<ActionResult> UpdatePart(int id, UpdatePartRequest request)
    {
        var part = await _context.VehicleParts.FindAsync(id);
        if (part == null)
            return NotFound();

        part.CategoryId = request.CategoryId;
        part.Name = request.Name;
        part.Description = request.Description;
        part.PartNumber = request.PartNumber;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("parts/{id}")]
    public async Task<ActionResult> DeletePart(int id)
    {
        var part = await _context.VehicleParts.FindAsync(id);
        if (part == null)
            return NotFound();

        part.IsActive = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

// DTOs
public record PartCategoryDto(int Id, string Name, string? Description, string? Icon, int PartsCount);
public record VehiclePartDto(int Id, int CategoryId, string Name, string? Description, string? PartNumber);
public record VehiclePartWithCategoryDto(int Id, int CategoryId, string CategoryName, string Name, string? Description, string? PartNumber);

// Request Records
public record CreateCategoryRequest(string Name, string? Description, string? Icon);
public record UpdateCategoryRequest(string Name, string? Description, string? Icon);
public record CreatePartRequest(int CategoryId, string Name, string? Description, string? PartNumber);
public record UpdatePartRequest(int CategoryId, string Name, string? Description, string? PartNumber);





using GisAPI.Application.Features.Admin.Brands;
using GisAPI.Attributes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Mutations du référentiel marques / modèles (données GLOBALES, partagées par toutes les
/// sociétés). Elles vivaient sous /api/brands, [Authorize] seul et exempté de
/// PermissionMiddleware : n'importe quel utilisateur client pouvait renommer ou désactiver
/// une marque pour toute la plateforme. Et l'écran /admin, qui n'a que admin_token, ne
/// pouvait plus les appeler depuis que l'intercepteur réserve ce jeton aux URL /api/admin
/// (401 silencieux, recette TN du 14/09/2026). Les lectures restent dans BrandsController.
/// </summary>
[ApiController]
[Route("api/admin/brands")]
[Authorize]
[RequireAdmin] // en plus de la garde /api/admin de PermissionMiddleware, qui laisse passer un jeton sans identifiant
public class AdminBrandsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminBrandsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateBrand([FromBody] AdminBrandRequest request, CancellationToken ct) =>
        ToResponse(await _mediator.Send(new CreateBrandCommand(request.Name, request.LogoUrl), ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateBrand(int id, [FromBody] AdminBrandRequest request, CancellationToken ct) =>
        ToResponse(await _mediator.Send(new UpdateBrandCommand(id, request.Name, request.LogoUrl, request.IsActive), ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeactivateBrand(int id, CancellationToken ct) =>
        ToResponse(await _mediator.Send(new DeactivateBrandCommand(id), ct));

    [HttpPost("{brandId:int}/models")]
    public async Task<IActionResult> CreateModel(int brandId, [FromBody] AdminVehicleModelRequest request, CancellationToken ct) =>
        ToResponse(await _mediator.Send(new CreateVehicleModelCommand(brandId, request.Name, request.VehicleType), ct));

    [HttpPut("models/{id:int}")]
    public async Task<IActionResult> UpdateModel(int id, [FromBody] AdminVehicleModelRequest request, CancellationToken ct) =>
        ToResponse(await _mediator.Send(new UpdateVehicleModelCommand(id, request.Name, request.VehicleType, request.IsActive), ct));

    [HttpDelete("models/{id:int}")]
    public async Task<IActionResult> DeactivateModel(int id, CancellationToken ct) =>
        ToResponse(await _mediator.Send(new DeactivateVehicleModelCommand(id), ct));

    /// <summary>Statut HTTP + corps { id, reactivated, message } : l'écran affiche le message tel quel.</summary>
    internal IActionResult ToResponse(BrandCatalogResult result)
    {
        var body = new AdminBrandCatalogResponse(result.Id, result.Status == BrandCatalogStatus.Reactivated, result.Message);
        return result.Status switch
        {
            BrandCatalogStatus.Created => StatusCode(StatusCodes.Status201Created, body),
            BrandCatalogStatus.Reactivated or BrandCatalogStatus.Updated or BrandCatalogStatus.Deactivated => Ok(body),
            BrandCatalogStatus.Invalid => BadRequest(body),
            BrandCatalogStatus.NotFound => NotFound(body),
            BrandCatalogStatus.Conflict => Conflict(body),
            _ => StatusCode(StatusCodes.Status500InternalServerError, body),
        };
    }
}

/// <summary>IsActive absent = inchangé (l'écran envoie true à l'enregistrement).</summary>
public record AdminBrandRequest(string? Name, string? LogoUrl, bool? IsActive = null);

public record AdminVehicleModelRequest(string? Name, string? VehicleType, bool? IsActive = null);

public record AdminBrandCatalogResponse(int? Id, bool Reactivated, string Message);

using MediatR;

namespace GisAPI.Application.Features.Admin.Brands;

// Mutations du référentiel marques / modèles — réservées à l'administrateur système
// (AdminBrandsController, /api/admin/brands). DELETE = désactivation (IsActive=false) :
// des véhicules portent déjà ces noms, rien n'est supprimé.
// IsActive null = inchangé : l'ancien contrat (bool obligatoire) désactivait une marque
// dès qu'un appelant omettait le champ.

public sealed record CreateBrandCommand(string? Name, string? LogoUrl) : IRequest<BrandCatalogResult>;

public sealed record UpdateBrandCommand(int Id, string? Name, string? LogoUrl, bool? IsActive) : IRequest<BrandCatalogResult>;

public sealed record DeactivateBrandCommand(int Id) : IRequest<BrandCatalogResult>;

public sealed record CreateVehicleModelCommand(int BrandId, string? Name, string? VehicleType) : IRequest<BrandCatalogResult>;

public sealed record UpdateVehicleModelCommand(int Id, string? Name, string? VehicleType, bool? IsActive) : IRequest<BrandCatalogResult>;

public sealed record DeactivateVehicleModelCommand(int Id) : IRequest<BrandCatalogResult>;

public enum BrandCatalogStatus
{
    Created,
    Reactivated,
    Updated,
    Deactivated,
    Invalid,
    NotFound,
    Conflict,
}

/// <summary>Issue d'une mutation, avec un message français destiné à l'écran admin.</summary>
public sealed record BrandCatalogResult(BrandCatalogStatus Status, string Message, int? Id = null)
{
    public bool Succeeded => Status is BrandCatalogStatus.Created or BrandCatalogStatus.Reactivated
        or BrandCatalogStatus.Updated or BrandCatalogStatus.Deactivated;
}

using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Brands;

/// <summary>
/// Création, modification et désactivation des marques et modèles, sans doublon :
/// un homonyme ACTIF (casse et espaces ignorés) est refusé, un homonyme INACTIF est
/// réactivé au lieu d'être recréé.
/// </summary>
public sealed class BrandCatalogCommandHandler :
    IRequestHandler<CreateBrandCommand, BrandCatalogResult>,
    IRequestHandler<UpdateBrandCommand, BrandCatalogResult>,
    IRequestHandler<DeactivateBrandCommand, BrandCatalogResult>,
    IRequestHandler<CreateVehicleModelCommand, BrandCatalogResult>,
    IRequestHandler<UpdateVehicleModelCommand, BrandCatalogResult>,
    IRequestHandler<DeactivateVehicleModelCommand, BrandCatalogResult>
{
    private const string BrandLabel = "de la marque";
    private const string ModelLabel = "du modèle";

    private readonly IGisDbContext _context;

    public BrandCatalogCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    // ── Marques ────────────────────────────────────────────────────────────────

    public async Task<BrandCatalogResult> Handle(CreateBrandCommand request, CancellationToken ct)
    {
        if (!BrandCatalogRules.TryNormalizeName(request.Name, BrandLabel, out var name, out var error)
            || !BrandCatalogRules.TryNormalizeLogoUrl(request.LogoUrl, out var logoUrl, out error))
            return Invalid(error!);

        var homonyms = await BrandHomonymsAsync(name, excludeId: null, ct);

        var active = homonyms.FirstOrDefault(b => b.IsActive);
        if (active != null)
            return Conflict($"La marque « {Show(active.Name)} » existe déjà.", active.Id);

        var inactive = homonyms.FirstOrDefault();
        if (inactive != null)
        {
            // Même identifiant qu'avant sa désactivation : ses modèles reviennent avec elle.
            // Nom stocké inchangé : les véhicules qui le portent en texte le retrouvent.
            inactive.IsActive = true;
            if (logoUrl != null)
                inactive.LogoUrl = logoUrl;
            await _context.SaveChangesAsync(ct);
            return new(BrandCatalogStatus.Reactivated,
                $"La marque « {Show(inactive.Name)} » existait déjà mais était désactivée : elle a été réactivée.",
                inactive.Id);
        }

        var brand = new Brand { Name = name, LogoUrl = logoUrl, IsActive = true, CreatedAt = DateTime.UtcNow };
        _context.Brands.Add(brand);
        await _context.SaveChangesAsync(ct);
        return new(BrandCatalogStatus.Created, $"La marque « {Show(brand.Name)} » a été créée.", brand.Id);
    }

    public async Task<BrandCatalogResult> Handle(UpdateBrandCommand request, CancellationToken ct)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, ct);
        if (brand == null)
            return NotFound("Marque introuvable.");

        if (!BrandCatalogRules.TryNormalizeName(request.Name, BrandLabel, out var name, out var error)
            || !BrandCatalogRules.TryNormalizeLogoUrl(request.LogoUrl, out var logoUrl, out error))
            return Invalid(error!);

        var willBeActive = request.IsActive ?? brand.IsActive;
        if (CouldCreateActiveHomonym(brand.Name, brand.IsActive, name, willBeActive))
        {
            var taken = (await BrandHomonymsAsync(name, excludeId: brand.Id, ct)).FirstOrDefault(b => b.IsActive);
            if (taken != null)
                return Conflict($"Le nom « {Show(taken.Name)} » est déjà pris par une autre marque.", taken.Id);
        }

        brand.Name = BrandCatalogRules.NameToStore(brand.Name, name);
        brand.LogoUrl = logoUrl;
        brand.IsActive = willBeActive;
        await _context.SaveChangesAsync(ct);
        return new(BrandCatalogStatus.Updated, $"La marque « {Show(brand.Name)} » a été enregistrée.", brand.Id);
    }

    public async Task<BrandCatalogResult> Handle(DeactivateBrandCommand request, CancellationToken ct)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, ct);
        if (brand == null)
            return NotFound("Marque introuvable.");

        brand.IsActive = false;
        await _context.SaveChangesAsync(ct);
        return new(BrandCatalogStatus.Deactivated, $"La marque « {Show(brand.Name)} » a été désactivée.", brand.Id);
    }

    // ── Modèles ────────────────────────────────────────────────────────────────

    public async Task<BrandCatalogResult> Handle(CreateVehicleModelCommand request, CancellationToken ct)
    {
        var brand = await _context.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BrandId, ct);
        if (brand == null)
            return NotFound("Marque introuvable.");

        if (!BrandCatalogRules.TryNormalizeName(request.Name, ModelLabel, out var name, out var error)
            || !BrandCatalogRules.TryNormalizeVehicleType(request.VehicleType, out var vehicleType, out error))
            return Invalid(error!);

        var homonyms = await ModelHomonymsAsync(brand.Id, name, excludeId: null, ct);

        var active = homonyms.FirstOrDefault(m => m.IsActive);
        if (active != null)
            return Conflict($"Le modèle « {Show(active.Name)} » existe déjà pour la marque « {Show(brand.Name)} ».", active.Id);

        var inactive = homonyms.FirstOrDefault();
        if (inactive != null)
        {
            // Nom stocké inchangé : les véhicules qui le portent en texte le retrouvent.
            inactive.IsActive = true;
            if (vehicleType != null)
                inactive.VehicleType = vehicleType;
            await _context.SaveChangesAsync(ct);
            return new(BrandCatalogStatus.Reactivated,
                $"Le modèle « {Show(inactive.Name)} » existait déjà pour la marque « {Show(brand.Name)} » mais était désactivé : il a été réactivé.",
                inactive.Id);
        }

        var model = new VehicleModel
        {
            BrandId = brand.Id,
            Name = name,
            VehicleType = vehicleType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        _context.VehicleModels.Add(model);
        await _context.SaveChangesAsync(ct);
        return new(BrandCatalogStatus.Created, $"Le modèle « {Show(model.Name)} » a été ajouté à la marque « {Show(brand.Name)} ».", model.Id);
    }

    public async Task<BrandCatalogResult> Handle(UpdateVehicleModelCommand request, CancellationToken ct)
    {
        var model = await _context.VehicleModels.FirstOrDefaultAsync(m => m.Id == request.Id, ct);
        if (model == null)
            return NotFound("Modèle introuvable.");

        if (!BrandCatalogRules.TryNormalizeName(request.Name, ModelLabel, out var name, out var error)
            || !BrandCatalogRules.TryNormalizeVehicleType(request.VehicleType, out var vehicleType, out error))
            return Invalid(error!);

        var willBeActive = request.IsActive ?? model.IsActive;
        if (CouldCreateActiveHomonym(model.Name, model.IsActive, name, willBeActive))
        {
            var taken = (await ModelHomonymsAsync(model.BrandId, name, excludeId: model.Id, ct)).FirstOrDefault(m => m.IsActive);
            if (taken != null)
                return Conflict($"Le nom « {Show(taken.Name)} » est déjà pris par un autre modèle de cette marque.", taken.Id);
        }

        model.Name = BrandCatalogRules.NameToStore(model.Name, name);
        model.VehicleType = vehicleType;
        model.IsActive = willBeActive;
        await _context.SaveChangesAsync(ct);
        return new(BrandCatalogStatus.Updated, $"Le modèle « {Show(model.Name)} » a été enregistré.", model.Id);
    }

    public async Task<BrandCatalogResult> Handle(DeactivateVehicleModelCommand request, CancellationToken ct)
    {
        var model = await _context.VehicleModels.FirstOrDefaultAsync(m => m.Id == request.Id, ct);
        if (model == null)
            return NotFound("Modèle introuvable.");

        model.IsActive = false;
        await _context.SaveChangesAsync(ct);
        return new(BrandCatalogStatus.Deactivated, $"Le modèle « {Show(model.Name)} » a été désactivé.", model.Id);
    }

    // ── Outils ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Le contrôle d'homonyme ne s'applique que si l'enregistrement peut FAIRE NAÎTRE un
    /// doublon actif : renommage (clé normalisée différente) ou réactivation. La base TN
    /// compte déjà 34 paires de modèles homonymes actifs (44 en comptant les inactifs,
    /// jeu initial rejoué) ; contrôler
    /// à chaque enregistrement interdirait de corriger le type de « Hiace » n° 62 tant que
    /// « Hiace » n° 5 existe.
    /// </summary>
    public static bool CouldCreateActiveHomonym(string currentName, bool currentlyActive, string newName, bool willBeActive) =>
        willBeActive && (!currentlyActive || !BrandCatalogRules.SameName(currentName, newName));

    // Table de quelques dizaines de lignes (27 marques en base TN) : la comparaison
    // normalisée se fait en mémoire, SQL ne sait pas reproduire NormalizeName à l'identique.
    // Tri par identifiant : on réactive toujours le plus ancien homonyme.
    private async Task<List<Brand>> BrandHomonymsAsync(string name, int? excludeId, CancellationToken ct)
    {
        var key = BrandCatalogRules.NameKey(name);
        var brands = await _context.Brands.OrderBy(b => b.Id).ToListAsync(ct);
        return brands.Where(b => b.Id != excludeId && BrandCatalogRules.NameKey(b.Name) == key).ToList();
    }

    private async Task<List<VehicleModel>> ModelHomonymsAsync(int brandId, string name, int? excludeId, CancellationToken ct)
    {
        var key = BrandCatalogRules.NameKey(name);
        var models = await _context.VehicleModels.Where(m => m.BrandId == brandId).OrderBy(m => m.Id).ToListAsync(ct);
        return models.Where(m => m.Id != excludeId && BrandCatalogRules.NameKey(m.Name) == key).ToList();
    }

    // Un nom stocké avec des espaces parasites (« ATECA  ») s'affiche nettoyé dans les messages.
    private static string Show(string name) => BrandCatalogRules.NormalizeName(name);

    private static BrandCatalogResult Invalid(string message) => new(BrandCatalogStatus.Invalid, message);

    private static BrandCatalogResult NotFound(string message) => new(BrandCatalogStatus.NotFound, message);

    private static BrandCatalogResult Conflict(string message, int existingId) => new(BrandCatalogStatus.Conflict, message, existingId);
}

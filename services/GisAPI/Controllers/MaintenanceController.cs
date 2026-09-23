using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Security;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;

namespace GisAPI.Controllers;

/// <summary>
/// Fiches d'entretien « libres » (table maintenance_records) : saisie depuis l'écran
/// Dépenses, lecture des entretiens planifiés.
///
/// PORTÉE VÉHICULES (incident HERTZ, 23/09/2026). Ce contrôleur ne filtrait que par
/// société, alors que ses frères (GetVehicleMaintenanceQueryHandler,
/// MaintenanceAlertsQueryHandlers, VehicleMaintenanceCommandHandlers) appliquent tous
/// <see cref="VehicleScope"/>. Chez un LOUEUR, les véhicules d'une même société sont
/// loués à des clients distincts : le filtre société ne cloisonne RIEN entre eux. Un
/// compte restreint ayant la case Entretien lisait donc les entretiens de tout le parc
/// — avec l'entité Vehicle COMPLÈTE (plaque, kilométrage, échéances) et les coûts
/// pièces / main-d'œuvre — et pouvait saisir un entretien sur le véhicule loué à un
/// autre client, ce qui faisait avancer SON compteur et créait une dépense à son nom.
///
/// La portée a TROIS états, à ne jamais confondre :
///   • <c>null</c>    → administrateur, AUCUN filtre (tout le parc, même sans aucune
///                      ligne dans user_vehicles — cas de l'utilisateur 11 de HERTZ) ;
///   • liste non vide → ses véhicules ;
///   • liste VIDE     → non-administrateur sans affectation, il ne voit RIEN.
///
/// Hors portée, la réponse est TOUJOURS celle d'un identifiant inexistant (404) : on ne
/// révèle jamais qu'une fiche ou un véhicule existe.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MaintenanceController : ControllerBase
{
    /// <summary>Même texte que les commandes d'entretien sœurs : le front l'affiche tel quel.</summary>
    internal const string VehiculeIntrouvable =
        "Ce véhicule est introuvable : il a peut-être été supprimé. Rechargez la page puis recommencez.";

    internal const string EntretienIntrouvable =
        "Cet entretien est introuvable : il a peut-être été supprimé. Rechargez la page puis recommencez.";

    private readonly GisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public MaintenanceController(GisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    private CancellationToken Annulation => HttpContext?.RequestAborted ?? CancellationToken.None;

    /// <summary>
    /// Véhicules visibles par l'appelant : <c>null</c> = administrateur (aucun filtre),
    /// liste non vide = ses véhicules, liste VIDE = rien.
    /// </summary>
    private Task<List<int>?> PorteeVehiculesAsync() =>
        VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, Annulation);

    /// <summary>
    /// Restreint une requête de fiches à la portée. La portée s'applique AVANT tout filtre
    /// optionnel, qui ne fait ensuite que l'INTERSECTER : sans vehicleId, c'est justement
    /// l'appel qui rendait tout le parc.
    /// </summary>
    private async Task<IQueryable<MaintenanceRecord>> DansLaPorteeAsync(IQueryable<MaintenanceRecord> query)
    {
        var portee = await PorteeVehiculesAsync();
        if (portee is null) return query;

        List<int> ids = portee;
        return query.Where(m => ids.Contains(m.VehicleId));
    }

    /// <summary>
    /// Ce que la réponse porte d'une fiche : ses champs propres, ses pièces et un résumé
    /// du véhicule (identifiant, nom, plaque). L'entité Vehicle brute n'est plus rendue :
    /// elle publiait kilométrage, échéances d'assurance et de visite technique, boîtier…
    /// Aucun écran ne lit plus que ces champs (modèle MaintenanceRecord de types.ts).
    /// </summary>
    private static readonly Expression<Func<MaintenanceRecord, FicheEntretienDto>> Projection = m => new FicheEntretienDto
    {
        Id = m.Id,
        VehicleId = m.VehicleId,
        CompanyId = m.CompanyId,
        Type = m.Type,
        Description = m.Description,
        MileageAtService = m.MileageAtService,
        Date = m.Date,
        NextServiceDate = m.NextServiceDate,
        NextServiceMileage = m.NextServiceMileage,
        Status = m.Status,
        LaborCost = m.LaborCost,
        PartsCost = m.PartsCost,
        TotalCost = m.TotalCost,
        ServiceProvider = m.ServiceProvider,
        ProviderContact = m.ProviderContact,
        InvoiceNumber = m.InvoiceNumber,
        InvoiceUrl = m.InvoiceUrl,
        Notes = m.Notes,
        SupplierId = m.SupplierId,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        Vehicle = m.Vehicle == null ? null : new VehiculeEntretienDto
        {
            Id = m.Vehicle.Id,
            Name = m.Vehicle.Name,
            Plate = m.Vehicle.Plate
        },
        Parts = m.Parts.Select(p => new PieceEntretienDto
        {
            Id = p.Id,
            MaintenanceRecordId = p.MaintenanceRecordId,
            Name = p.Name,
            PartNumber = p.PartNumber,
            Quantity = p.Quantity,
            UnitCost = p.UnitCost,
            TotalCost = p.TotalCost
        }).ToList()
    };

    private static readonly Func<MaintenanceRecord, FicheEntretienDto> ProjectionEnMemoire = Projection.Compile();

    private static PieceEntretienDto PieceDto(MaintenancePart p) => new()
    {
        Id = p.Id,
        MaintenanceRecordId = p.MaintenanceRecordId,
        Name = p.Name,
        PartNumber = p.PartNumber,
        Quantity = p.Quantity,
        UnitCost = p.UnitCost,
        TotalCost = p.TotalCost
    };

    /// <summary>
    /// Fiche de la société sur laquelle l'appelant peut ÉCRIRE. Inexistante, d'une autre
    /// société ou rattachée à un véhicule hors portée : la même <see cref="NotFoundException"/>
    /// (404, même message), pour ne jamais révéler qu'elle existe.
    /// </summary>
    private async Task<MaintenanceRecord> FicheModifiableAsync(int id)
    {
        var companyId = GetCompanyId();

        var record = await _context.MaintenanceRecords
            .FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId, Annulation)
            ?? throw new NotFoundException(EntretienIntrouvable);

        await VehicleScope.EnsureCanWriteAsync(_context, _tenantService, record.VehicleId,
            EntretienIntrouvable, Annulation);

        return record;
    }

    [HttpGet]
    public async Task<ActionResult<List<FicheEntretienDto>>> GetMaintenanceRecords([FromQuery] int? vehicleId = null)
    {
        var companyId = GetCompanyId();

        var query = await DansLaPorteeAsync(_context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.CompanyId == companyId));

        if (vehicleId.HasValue)
            query = query.Where(m => m.VehicleId == vehicleId);

        var records = await query
            .OrderByDescending(m => m.Date)
            .Select(Projection)
            .ToListAsync(Annulation);

        return Ok(records);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<FicheEntretienDto>> GetMaintenanceRecord(int id)
    {
        var companyId = GetCompanyId();

        // IDOR : il suffisait de changer l'identifiant dans l'URL pour lire la fiche
        // (coûts, prestataire, facture) du véhicule loué à un autre client. La portée
        // entre dans la requête : hors portée, même 404 qu'une fiche inexistante.
        var query = await DansLaPorteeAsync(_context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.Id == id && m.CompanyId == companyId));

        var record = await query
            .Select(Projection)
            .FirstOrDefaultAsync(Annulation);

        if (record == null)
            return NotFound();

        return Ok(record);
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<FicheEntretienDto>>> GetUpcomingMaintenance()
    {
        var companyId = GetCompanyId();
        var nextMonth = DateTime.UtcNow.AddMonths(1);

        var query = await DansLaPorteeAsync(_context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.CompanyId == companyId &&
                        m.Status == "scheduled" &&
                        m.Date <= nextMonth));

        var records = await query
            .OrderBy(m => m.Date)
            .Select(Projection)
            .ToListAsync(Annulation);

        return Ok(records);
    }

    [HttpPost]
    public async Task<ActionResult<FicheEntretienDto>> CreateMaintenanceRecord([FromBody] MaintenanceRecord record)
    {
        var companyId = GetCompanyId();

        // Sur-affectation : le corps est lié à l'ENTITÉ, navigations comprises. Add() insère
        // tout le graphe reçu — un « vehicle » joint au corps créait un véhicule, ou, par
        // correction de clé étrangère, substituait son identifiant au vehicleId
        // contrôlé. On ne garde que les champs scalaires de la fiche et de ses pièces.
        record.Id = 0;
        record.Vehicle = null;
        record.Supplier = null;
        record.Societe = null;
        record.Parts ??= new List<MaintenancePart>();   // « "parts": null » dans le corps
        foreach (var piece in record.Parts)
        {
            piece.Id = 0;
            piece.MaintenanceRecord = null;
        }

        // Le véhicule doit appartenir à la société : l'administrateur (portée null) n'est
        // filtré par rien d'autre, et le filtre multi-tenant est contourné pour
        // l'administrateur système. Une fiche — et sa dépense — pouvaient sinon viser le
        // véhicule d'une autre société.
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == record.VehicleId && v.CompanyId == companyId, Annulation)
            ?? throw new NotFoundException(VehiculeIntrouvable);

        // Portée véhicules, comme « marquer fait » et POST /api/costs : l'entretien fait
        // avancer le compteur et crée une dépense. Un compte restreint ne l'enregistre que
        // sur ses véhicules ; hors portée, même 404 qu'un véhicule inexistant. Contrôle
        // AVANT toute écriture : rien n'est ajouté au contexte en cas de refus.
        await VehicleScope.EnsureCanWriteAsync(_context, _tenantService, vehicle.Id,
            VehiculeIntrouvable, Annulation);

        // Même règle que « marquer fait » : le fournisseur doit être de la société.
        if (record.SupplierId.HasValue
            && !await _context.Suppliers.AnyAsync(
                s => s.Id == record.SupplierId.Value && s.CompanyId == companyId, Annulation))
        {
            throw new DomainException(
                "Le fournisseur choisi n'existe plus. Rechargez la page et sélectionnez-le à nouveau.");
        }

        record.CompanyId = companyId;
        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;
        record.TotalCost = record.LaborCost + record.PartsCost;

        _context.MaintenanceRecords.Add(record);

        // Le kilométrage relevé à l'entretien fait avancer la fiche véhicule,
        // comme le fait « marquer fait » — sans lui, un entretien saisi depuis
        // l'écran Dépenses laissait le compteur figé (recette du 08/09/2026).
        VehicleMileage.Advance(vehicle, record.MileageAtService);

        // Un entretien RÉALISÉ est une dépense : il lui faut sa ligne dans
        // vehicle_costs, seule table que lisent l'écran Dépenses, le tableau de
        // bord et les rapports. Sans elle, l'entretien saisi depuis « Nouvelle
        // dépense > Entretien » n'apparaissait NULLE PART (recette du
        // 08/09/2026 : maintenance_records n'est lue par aucun écran de dépense).
        // Même motif que « marquer fait », qui crée déjà la dépense.
        if (string.Equals(record.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            _context.VehicleCosts.Add(new VehicleCost
            {
                VehicleId = record.VehicleId,
                Type = "maintenance",
                Description = string.IsNullOrWhiteSpace(record.Description)
                    ? "Entretien"
                    : $"Entretien: {record.Description}",
                Amount = record.TotalCost,
                Date = record.Date,
                Mileage = record.MileageAtService > 0 ? record.MileageAtService : null,
                ReceiptNumber = record.InvoiceNumber,
                CompanyId = companyId
            });
        }

        await _context.SaveChangesAsync(Annulation);

        // La réponse suit la même projection que les lectures : plus d'entité Vehicle brute
        // (le suivi EF la rattachait à la fiche au moment du SaveChanges).
        return CreatedAtAction(nameof(GetMaintenanceRecord), new { id = record.Id }, ProjectionEnMemoire(record));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateMaintenanceRecord(int id, [FromBody] MaintenanceRecord updated)
    {
        // Hors portée : même 404 qu'une fiche inexistante. Le véhicule de la fiche n'est
        // jamais repris du corps (updated.VehicleId est ignoré) : on ne « déplace » pas un
        // entretien vers un autre véhicule.
        var record = await FicheModifiableAsync(id);

        record.Type = updated.Type;
        record.Description = updated.Description;
        record.MileageAtService = updated.MileageAtService;
        record.Date = updated.Date;
        record.NextServiceDate = updated.NextServiceDate;
        record.NextServiceMileage = updated.NextServiceMileage;
        record.Status = updated.Status;
        record.LaborCost = updated.LaborCost;
        record.PartsCost = updated.PartsCost;
        record.TotalCost = updated.LaborCost + updated.PartsCost;
        record.ServiceProvider = updated.ServiceProvider;
        record.ProviderContact = updated.ProviderContact;
        record.InvoiceNumber = updated.InvoiceNumber;
        record.InvoiceUrl = updated.InvoiceUrl;
        record.Notes = updated.Notes;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(Annulation);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMaintenanceRecord(int id)
    {
        // Hors portée : même 404 qu'une fiche inexistante — un locataire ne supprime pas
        // l'historique d'entretien du véhicule loué à un autre client.
        var record = await FicheModifiableAsync(id);

        _context.MaintenanceRecords.Remove(record);
        await _context.SaveChangesAsync(Annulation);

        return NoContent();
    }

    [HttpPost("{id}/parts")]
    public async Task<ActionResult<PieceEntretienDto>> AddPart(int id, [FromBody] MaintenancePart part)
    {
        // Hors portée : même 404 qu'une fiche inexistante. L'ajout d'une pièce modifie le
        // coût de la fiche, c'est une écriture sur le véhicule.
        var record = await FicheModifiableAsync(id);

        // Sur-affectation : seule la pièce est insérée, jamais une fiche jointe au corps.
        part.Id = 0;
        part.MaintenanceRecord = null;
        part.MaintenanceRecordId = id;
        part.TotalCost = part.UnitCost * part.Quantity;

        _context.MaintenanceParts.Add(part);

        record.PartsCost += part.TotalCost;
        record.TotalCost = record.LaborCost + record.PartsCost;

        await _context.SaveChangesAsync(Annulation);

        return Ok(PieceDto(part));
    }
}

/// <summary>
/// Fiche d'entretien telle que l'API la rend : les champs du modèle MaintenanceRecord
/// du front (types.ts), plus un résumé du véhicule à la place de l'entité complète.
/// </summary>
public class FicheEntretienDto
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public int CompanyId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MileageAtService { get; set; }
    public DateTime Date { get; set; }
    public DateTime? NextServiceDate { get; set; }
    public int? NextServiceMileage { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal LaborCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? ServiceProvider { get; set; }
    public string? ProviderContact { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceUrl { get; set; }
    public string? Notes { get; set; }
    public int? SupplierId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public VehiculeEntretienDto? Vehicle { get; set; }
    public List<PieceEntretienDto> Parts { get; set; } = new();
}

/// <summary>Résumé du véhicule d'une fiche : de quoi l'afficher, rien de plus.</summary>
public class VehiculeEntretienDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Plate { get; set; }
}

public class PieceEntretienDto
{
    public int Id { get; set; }
    public int MaintenanceRecordId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PartNumber { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

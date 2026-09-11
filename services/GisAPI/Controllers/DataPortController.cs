using ClosedXML.Excel;
using GisAPI.Application.Common;
using GisAPI.Application.Features.DataPort;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Attributes;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

/// <summary>
/// Import / export des données de la société au format Excel (recette client du
/// 25/08/2026). Quatre feuilles : Véhicules, Entretiens, Réparations, Carburant
/// (la feuille Réparations date de la recette du 11/09/2026 : les réparations
/// n'étaient ni exportées ni importables).
///
/// <para>Pensé d'abord pour l'offre « gestion de parc sans GPS » : démarrer un
/// parc en masse (import des véhicules) et récupérer ses données (export). Le
/// modèle téléchargeable a EXACTEMENT le format attendu par l'import — le client
/// exporte, complète, réimporte.</para>
///
/// <para>L'import ne fait que CRÉER : il n'écrase ni ne supprime rien. Un
/// véhicule dont le matricule existe déjà est ignoré (compté à part) ; un
/// entretien / une réparation / un plein dont le matricule est inconnu est ignoré
/// et signalé. Une réparation déjà présente (même référence, ou même véhicule,
/// jour, montant et description) est ignorée. Le résultat détaille ce qui a été
/// créé et ce qui a été écarté, avec la raison.</para>
/// </summary>
[ApiController]
[Route("api/dataport")]
[Authorize]
[RequireCompanyAdmin]
public class DataPortController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ILogger<DataPortController> _logger;

    // Import et export portent sur TOUT le parc (création de véhicules, dépenses, pleins,
    // réparations) : réservés à l'administrateur de la société. Avant le 11/09/2026, un
    // employé limité à deux véhicules pouvait exporter les montants de tout le parc et
    // importer sur des véhicules hors de sa portée — l'écran Données s'affichait pour tous.
    public DataPortController(GisDbContext context, ILogger<DataPortController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    // En-têtes partagés par l'export ET le modèle d'import : une seule source de
    // vérité pour que les deux formats coïncident.
    private static readonly string[] VehicleCols =
        { "Matricule", "Nom", "Marque", "Modèle", "Année", "Type", "Carburant", "Kilométrage", "Capacité réservoir (L)" };
    private static readonly string[] MaintenanceCols =
        { "Matricule", "Date (JJ/MM/AAAA)", "Intitulé", "Coût" };
    private static readonly string[] FuelCols =
        { "Matricule", "Date (JJ/MM/AAAA)", "Volume (L)", "Prix/L", "Montant total", "Kilométrage" };
    // « N° facture » = champ « Référence » de l'écran Dépenses (invoice_number).
    // « Référence » = référence interne REP-… : remplie par l'export, à laisser
    // vide dans le modèle ; elle sert à ignorer une réparation déjà présente.
    private static readonly string[] RepairCols =
        { "Matricule", "Date (JJ/MM/AAAA)", "Description", "Type", "Kilométrage", "Main d'œuvre", "Pièces",
          "Total", "Statut", "Fournisseur", "N° facture", "Référence" };

    // ─────────────────────────────── EXPORT ───────────────────────────────
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var companyId = GetCompanyId();

        var vehicles = await _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .OrderBy(v => v.Plate)
            .ToListAsync();
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var plateById = vehicles.ToDictionary(v => v.Id, v => v.Plate ?? v.Name);

        // Entretiens = dépenses de type « maintenance »/« entretien » (même
        // source que le tableau de bord et le rapport de coûts).
        var maintenance = await _context.VehicleCosts.AsNoTracking()
            .Where(m => m.CompanyId == companyId && vehicleIds.Contains(m.VehicleId)
                        && (m.Type == "maintenance" || m.Type == "entretien"))
            .OrderByDescending(m => m.Date)
            .Select(m => new { m.VehicleId, DoneDate = m.Date, Name = m.Description, ActualCost = m.Amount })
            .ToListAsync();

        var fuel = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId && f.VehicleId != null && vehicleIds.Contains(f.VehicleId.Value))
            .OrderByDescending(f => f.InvoiceDate)
            .Select(f => new { f.VehicleId, f.InvoiceDate, f.Volume, f.PricePerLiter, f.TotalAmount, f.OdometerKm })
            .ToListAsync();

        // Réparations : filtre société EXPLICITE, l'entité Repair n'a pas de filtre
        // de requête multi-tenant dans GisDbContext.
        var repairs = await _context.Repairs.AsNoTracking()
            .Where(x => x.SocieteId == companyId && vehicleIds.Contains(x.VehicleId))
            .OrderByDescending(x => x.RepairDate)
            .Select(x => new
            {
                x.VehicleId, x.RepairDate, x.Description, x.RepairType, x.MileageAtRepair,
                x.LaborCost, x.PartsCost, x.TotalCost, x.Status, x.SupplierId, x.InvoiceNumber, x.Reference
            })
            .ToListAsync();
        var supplierNames = await _context.Suppliers.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        using var wb = new XLWorkbook();

        var vs = wb.Worksheets.Add("Véhicules");
        WriteHeader(vs, VehicleCols);
        var r = 2;
        foreach (var v in vehicles)
        {
            vs.Cell(r, 1).Value = v.Plate ?? "";
            vs.Cell(r, 2).Value = v.Name ?? "";
            vs.Cell(r, 3).Value = v.Brand ?? "";
            vs.Cell(r, 4).Value = v.Model ?? "";
            vs.Cell(r, 5).Value = v.Year;
            vs.Cell(r, 6).Value = v.Type ?? "";
            vs.Cell(r, 7).Value = v.FuelType ?? "";
            vs.Cell(r, 8).Value = v.Mileage;
            vs.Cell(r, 9).Value = v.FuelTankCapacity;
            r++;
        }
        vs.Columns().AdjustToContents();

        var ms = wb.Worksheets.Add("Entretiens");
        WriteHeader(ms, MaintenanceCols);
        r = 2;
        foreach (var m in maintenance)
        {
            ms.Cell(r, 1).Value = plateById.GetValueOrDefault(m.VehicleId, "");
            ms.Cell(r, 2).Value = m.DoneDate.ToString("dd/MM/yyyy");
            ms.Cell(r, 3).Value = m.Name ?? "";
            ms.Cell(r, 4).Value = m.ActualCost;
            r++;
        }
        ms.Columns().AdjustToContents();

        var rps = wb.Worksheets.Add("Réparations");
        WriteHeader(rps, RepairCols);
        r = 2;
        foreach (var x in repairs)
        {
            rps.Cell(r, 1).Value = plateById.GetValueOrDefault(x.VehicleId, "");
            rps.Cell(r, 2).Value = x.RepairDate.ToString("dd/MM/yyyy");
            rps.Cell(r, 3).Value = x.Description ?? "";
            rps.Cell(r, 4).Value = string.IsNullOrWhiteSpace(x.RepairType) ? "" : RepairTypeClassifier.Label(x.RepairType);
            rps.Cell(r, 5).Value = x.MileageAtRepair;
            rps.Cell(r, 6).Value = x.LaborCost;
            rps.Cell(r, 7).Value = x.PartsCost;
            rps.Cell(r, 8).Value = x.TotalCost;
            rps.Cell(r, 9).Value = RepairImportRow.StatusLabel(x.Status);
            rps.Cell(r, 10).Value = x.SupplierId is int sid ? supplierNames.GetValueOrDefault(sid, "") : "";
            rps.Cell(r, 11).Value = x.InvoiceNumber ?? "";
            rps.Cell(r, 12).Value = x.Reference ?? "";
            r++;
        }
        rps.Columns().AdjustToContents();

        var fs = wb.Worksheets.Add("Carburant");
        WriteHeader(fs, FuelCols);
        r = 2;
        foreach (var f in fuel)
        {
            fs.Cell(r, 1).Value = f.VehicleId != null ? plateById.GetValueOrDefault(f.VehicleId.Value, "") : "";
            fs.Cell(r, 2).Value = f.InvoiceDate.ToString("dd/MM/yyyy");
            fs.Cell(r, 3).Value = f.Volume;
            fs.Cell(r, 4).Value = f.PricePerLiter;
            fs.Cell(r, 5).Value = f.TotalAmount;
            fs.Cell(r, 6).Value = f.OdometerKm;
            r++;
        }
        fs.Columns().AdjustToContents();

        return WorkbookFile(wb, $"calypso-donnees-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ───────────────────────────── MODÈLE VIDE ─────────────────────────────
    [HttpGet("template")]
    public IActionResult Template()
    {
        using var wb = new XLWorkbook();

        var vs = wb.Worksheets.Add("Véhicules");
        WriteHeader(vs, VehicleCols);
        vs.Cell(2, 1).Value = "123 TU 4567"; vs.Cell(2, 2).Value = "Camion 1";
        vs.Cell(2, 3).Value = "Renault"; vs.Cell(2, 4).Value = "Master";
        vs.Cell(2, 5).Value = 2021; vs.Cell(2, 6).Value = "camion";
        vs.Cell(2, 7).Value = "diesel"; vs.Cell(2, 8).Value = 145000; vs.Cell(2, 9).Value = 80;
        vs.Row(2).Style.Font.Italic = true;
        vs.Columns().AdjustToContents();

        var ms = wb.Worksheets.Add("Entretiens");
        WriteHeader(ms, MaintenanceCols);
        ms.Cell(2, 1).Value = "123 TU 4567"; ms.Cell(2, 2).Value = "15/08/2026";
        ms.Cell(2, 3).Value = "Vidange + filtres"; ms.Cell(2, 4).Value = 350;
        ms.Row(2).Style.Font.Italic = true;
        ms.Columns().AdjustToContents();

        // Fournisseur, N° facture et Référence laissés vides : la référence REP-…
        // est attribuée à l'import.
        var rps = wb.Worksheets.Add("Réparations");
        WriteHeader(rps, RepairCols);
        rps.Cell(2, 1).Value = "123 TU 4567"; rps.Cell(2, 2).Value = "18/08/2026";
        rps.Cell(2, 3).Value = "Plaquettes de frein AV"; rps.Cell(2, 4).Value = "Freinage";
        rps.Cell(2, 5).Value = 145100; rps.Cell(2, 6).Value = 80; rps.Cell(2, 7).Value = 120;
        rps.Cell(2, 8).Value = 200; rps.Cell(2, 9).Value = "Terminée";
        rps.Row(2).Style.Font.Italic = true;
        rps.Columns().AdjustToContents();

        var fs = wb.Worksheets.Add("Carburant");
        WriteHeader(fs, FuelCols);
        fs.Cell(2, 1).Value = "123 TU 4567"; fs.Cell(2, 2).Value = "20/08/2026";
        fs.Cell(2, 3).Value = 45; fs.Cell(2, 4).Value = 2.2; fs.Cell(2, 5).Value = 99; fs.Cell(2, 6).Value = 145200;
        fs.Row(2).Style.Font.Italic = true;
        fs.Columns().AdjustToContents();

        return WorkbookFile(wb, "calypso-modele-import.xlsx");
    }

    // ─────────────────────────────── IMPORT ───────────────────────────────
    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier reçu." });
        if (file.Length > 8 * 1024 * 1024)
            return BadRequest(new { message = "Fichier trop volumineux (8 Mo maximum)." });

        var companyId = GetCompanyId();
        XLWorkbook wb;
        try { using var stream = file.OpenReadStream(); wb = new XLWorkbook(stream); }
        catch { return BadRequest(new { message = "Fichier illisible : utilisez le modèle Excel fourni." }); }

        var result = new ImportSummary();

        // Matricules déjà connus (dédup + résolution pour entretiens/réparations/pleins).
        var byPlate = await _context.Vehicles
            .Where(v => v.CompanyId == companyId && v.Plate != null)
            .ToDictionaryAsync(v => Normalize(v.Plate!), v => v);

        // Véhicules dont on a importé au moins un relevé compteur : on contrôle
        // la cohérence de leur série une fois l'import terminé.
        var touchedVehicleIds = new HashSet<int>();

        // 1) Véhicules
        var vs = FindSheet(wb, "Véhicules");
        if (vs != null)
        {
            foreach (var row in DataRows(vs))
            {
                var plate = Str(row.Cell(1));
                if (string.IsNullOrWhiteSpace(plate)) continue;
                var key = Normalize(plate);
                if (byPlate.TryGetValue(key, out var known))
                {
                    // Le matricule existe déjà : on ne recrée pas le véhicule, mais on
                    // accepte le kilométrage du fichier s'il fait avancer le compteur.
                    // Auparavant la ligne était purement ignorée, si bien que corriger
                    // la colonne « km » et ré-importer ne changeait rien (recette du 08/09/2026).
                    if (VehicleMileage.Advance(known, Int(row.Cell(8))))
                    {
                        result.VehiclesUpdated++;
                        result.AddNote($"Véhicule « {plate} » : kilométrage porté à {known.Mileage:N0} km.");
                    }
                    else
                    {
                        result.VehiclesIgnored++;
                        result.AddNote($"Véhicule « {plate} » ignoré : matricule déjà présent.");
                    }
                    continue;
                }

                var vehicle = new Vehicle
                {
                    CompanyId = companyId,
                    Plate = plate,
                    Name = Str(row.Cell(2), plate),
                    Brand = Str(row.Cell(3)),
                    Model = Str(row.Cell(4)),
                    Year = Int(row.Cell(5)),
                    Type = Str(row.Cell(6), "camion"),
                    FuelType = Str(row.Cell(7), "diesel"),
                    Mileage = Int(row.Cell(8)) ?? 0,
                    FuelTankCapacity = Int(row.Cell(9)),
                    Status = "available",
                    AcquisitionType = "purchase"
                };
                _context.Vehicles.Add(vehicle);
                byPlate[key] = vehicle;
                result.VehiclesCreated++;
            }
            // On enregistre pour que les véhicules aient un Id avant de rattacher
            // les entretiens, réparations et pleins par matricule.
            await _context.SaveChangesAsync();
        }

        // 2) Entretiens
        var ms = FindSheet(wb, "Entretiens");
        if (ms != null)
        {
            foreach (var row in DataRows(ms))
            {
                var plate = Str(row.Cell(1));
                if (string.IsNullOrWhiteSpace(plate)) continue;
                if (!byPlate.TryGetValue(Normalize(plate), out var vehicle))
                { result.MaintenanceIgnored++; result.AddNote($"Entretien ignoré : matricule « {plate} » introuvable."); continue; }

                var date = Date(row.Cell(2));
                if (date == null) { result.MaintenanceIgnored++; result.AddNote($"Entretien « {plate} » ignoré : date invalide."); continue; }

                _context.VehicleCosts.Add(new VehicleCost
                {
                    CompanyId = companyId,
                    VehicleId = vehicle.Id,
                    Type = "maintenance",
                    Date = DateTime.SpecifyKind(date.Value, DateTimeKind.Utc),
                    Description = Str(row.Cell(3), "Entretien"),
                    Amount = Dec(row.Cell(4)) ?? 0
                });
                result.MaintenanceCreated++;
            }
        }

        // 3) Réparations (recette du 11/09/2026 : la table repairs n'était branchée
        // ni à l'export ni à l'import). Insertion directe et non par MediatR :
        // CreateRepairCommand ferait deux SaveChanges et publierait UNE notification
        // admin par ligne importée.
        var rs = FindSheet(wb, "Réparations");
        if (rs != null)
        {
            var existingRepairs = await _context.Repairs.AsNoTracking()
                .Where(x => x.SocieteId == companyId)
                .Select(x => new { x.Reference, x.VehicleId, x.RepairDate, x.TotalCost, x.Description })
                .ToListAsync();

            // Dédoublonnage : par référence REP-… (boucle exporter → compléter →
            // réimporter), sinon par clé naturelle pour les lignes sans référence.
            // Les deux ensembles s'enrichissent au fil du fichier : une ligne
            // répétée dans le même fichier n'est créée qu'une fois.
            var existingRefs = new HashSet<string>(
                existingRepairs.Select(x => (x.Reference ?? "").Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            var existingKeys = new HashSet<string>(existingRepairs.Select(x =>
                RepairImportRow.NaturalKey(x.VehicleId, x.RepairDate, x.TotalCost, x.Description)));

            // Même numérotation que la saisie à l'écran (nombre de réparations + 1),
            // en sautant une référence déjà prise (possible après des suppressions).
            var refSequence = existingRepairs.Count;

            // Fournisseur retrouvé par son nom, casse et accents ignorés ; jamais créé.
            var supplierByName = new Dictionary<string, int>();
            var suppliers = await _context.Suppliers.AsNoTracking()
                .Where(s => s.CompanyId == companyId)
                .OrderByDescending(s => s.IsActive).ThenBy(s => s.Id)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync();
            foreach (var s in suppliers)
                supplierByName.TryAdd(RepairImportRow.NormalizeKey(s.Name), s.Id);

            var now = DateTime.UtcNow;
            foreach (var row in DataRows(rs))
            {
                var plate = Str(row.Cell(1));
                if (string.IsNullOrWhiteSpace(plate)) continue;
                if (!byPlate.TryGetValue(Normalize(plate), out var vehicle))
                { result.RepairsIgnored++; result.AddNote($"Réparation ignorée : matricule « {plate} » introuvable."); continue; }

                var date = Date(row.Cell(2));
                if (date == null) { result.RepairsIgnored++; result.AddNote($"Réparation « {plate} » ignorée : date invalide."); continue; }
                var label = $"Réparation « {plate} » du {date.Value:dd/MM/yyyy}";

                // Un montant saisi mais illisible (« 1 200,50 » en texte, « 200 DT ») écarte la ligne :
                // l'importer à 0 puis réimporter le fichier corrigé créait un doublon (clé naturelle
                // différente). Une cellule VIDE reste permise (aucun montant = 0).
                var unreadable = new[] { (6, "main d'œuvre"), (7, "pièces"), (8, "total") }.FirstOrDefault(c => Unreadable(row.Cell(c.Item1)));
                if (unreadable.Item2 != null)
                { result.RepairsIgnored++; result.AddNote($"{label} ignorée : montant {unreadable.Item2} « {Str(row.Cell(unreadable.Item1))} » illisible."); continue; }

                var analysis = RepairImportRow.Analyze(
                    Dec(row.Cell(6)), Dec(row.Cell(7)), Dec(row.Cell(8)), Str(row.Cell(9)), Str(row.Cell(4)));
                if (!analysis.IsValid)
                { result.RepairsIgnored++; result.AddNote($"{label} ignorée : {analysis.Error}"); continue; }

                var description = RepairImportRow.Truncate(Str(row.Cell(3), "Réparation"), RepairImportRow.DescriptionMaxLength)!;
                var reference = RepairImportRow.Truncate(Str(row.Cell(12)), RepairImportRow.ReferenceMaxLength)!;
                if (reference.Length > 0 && existingRefs.Contains(reference))
                { result.RepairsIgnored++; result.AddNote($"{label} ignorée : référence « {reference} » déjà présente."); continue; }
                if (!existingKeys.Add(RepairImportRow.NaturalKey(vehicle.Id, date.Value, analysis.TotalCost, description)))
                {
                    result.RepairsIgnored++;
                    result.AddNote($"{label} ignorée : déjà présente (même véhicule, date, montant et description).");
                    continue;
                }

                if (reference.Length == 0)
                {
                    do { reference = RepairImportRow.GeneratedReference(now, ++refSequence); }
                    while (existingRefs.Contains(reference));
                }
                existingRefs.Add(reference);

                // Un fournisseur inconnu n'écarte pas la ligne : la réparation est
                // importée sans fournisseur et le client est prévenu.
                int? supplierId = null;
                var supplierName = Str(row.Cell(10));
                if (supplierName.Length > 0)
                {
                    if (supplierByName.TryGetValue(RepairImportRow.NormalizeKey(supplierName), out var sid)) supplierId = sid;
                    else result.AddNote($"{label} : fournisseur « {supplierName} » introuvable, importée sans fournisseur.");
                }


                var mileage = Int(row.Cell(5)) is int km && km > 0 ? km : (int?)null;
                var invoiceNumber = Str(row.Cell(11));

                var repair = new Repair
                {
                    SocieteId = companyId,
                    VehicleId = vehicle.Id,
                    SupplierId = supplierId,
                    Reference = reference,
                    Description = description,
                    RepairDate = DateTime.SpecifyKind(date.Value, DateTimeKind.Utc),
                    MileageAtRepair = mileage,
                    LaborCost = analysis.LaborCost,
                    PartsCost = analysis.PartsCost,
                    TotalCost = analysis.TotalCost,
                    Status = analysis.Status,
                    RepairType = analysis.RepairType,
                    InvoiceNumber = invoiceNumber.Length > 0
                        ? RepairImportRow.Truncate(invoiceNumber, RepairImportRow.InvoiceNumberMaxLength)
                        : null,
                    CreatedAt = now
                };
                // Une ligne de pièce technique porte le montant « Pièces » : sans elle,
                // la première modification à l'écran le remettrait à zéro.
                if (RepairImportRow.PartLine(analysis) is { } part)
                    repair.Parts.Add(part);
                _context.Repairs.Add(repair);
                result.RepairsCreated++;
                foreach (var note in analysis.Notes)
                    result.AddNote($"{label} : {note}");

                // Même règle que la saisie à l'écran : le relevé fait avancer la fiche
                // véhicule, sauf pour une réparation annulée (aucun passage à l'atelier).
                if (mileage != null && analysis.AdvancesMileage)
                {
                    VehicleMileage.Advance(vehicle, mileage);
                    touchedVehicleIds.Add(vehicle.Id);
                }
            }
        }

        // 4) Carburant
        var fs = FindSheet(wb, "Carburant");
        if (fs != null)
        {
            var defaultFuelTypeId = await _context.FuelTypes
                .OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync() ?? 1;
            foreach (var row in DataRows(fs))
            {
                var plate = Str(row.Cell(1));
                if (string.IsNullOrWhiteSpace(plate)) continue;
                if (!byPlate.TryGetValue(Normalize(plate), out var vehicle))
                { result.FuelIgnored++; result.AddNote($"Plein ignoré : matricule « {plate} » introuvable."); continue; }

                var date = Date(row.Cell(2));
                if (date == null) { result.FuelIgnored++; result.AddNote($"Plein « {plate} » ignoré : date invalide."); continue; }

                var volume = Dec(row.Cell(3)) ?? 0;
                var price = Dec(row.Cell(4)) ?? 0;
                var total = Dec(row.Cell(5)) ?? (volume * price);

                var odometer = Int(row.Cell(6)) is int km && km > 0 ? km : (int?)null;

                _context.FuelEntries.Add(new FuelEntry
                {
                    CompanyId = companyId,
                    VehicleId = vehicle.Id,
                    VehiclePlate = vehicle.Plate ?? plate,
                    FuelTypeId = defaultFuelTypeId,
                    Volume = volume,
                    PricePerLiter = price,
                    TotalAmount = total,
                    InvoiceDate = DateTime.SpecifyKind(date.Value, DateTimeKind.Utc),
                    OdometerKm = odometer,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                result.FuelCreated++;

                // Le relevé fait avancer la fiche véhicule, comme à la saisie manuelle.
                // Sans cela, un client sans boîtier importait ses tickets et voyait son
                // kilométrage figé (recette du 08/09/2026). On ne refuse RIEN ici :
                // un import est de l'historique, ses relevés sont normalement INFÉRIEURS
                // au compteur courant — appliquer la garde de la saisie manuelle
                // rejetterait la quasi-totalité des lignes légitimes.
                VehicleMileage.Advance(vehicle, odometer);
                touchedVehicleIds.Add(vehicle.Id);
            }
        }

        await _context.SaveChangesAsync();

        // Les relevés importés ne sont pas contrôlés à la saisie : on signale après coup
        // ceux que la série rend invalides (même règle d'aberration que l'écran Carburant),
        // pour que le client sache quoi corriger plutôt que de découvrir un compteur faux.
        // La série est celle de TOUS les relevés saisis (pleins, entretiens, réparations,
        // dépenses — un par jour), la source unique des rapports : depuis la feuille
        // Réparations, un relevé importé peut aussi venir d'une réparation.
        if (touchedVehicleIds.Count > 0)
        {
            var readingsByVehicle = await OdometerReadings.LoadAsync(
                _context, companyId, touchedVehicleIds.ToList(),
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1),
                HttpContext.RequestAborted);

            foreach (var vehicleId in touchedVehicleIds)
            {
                var distance = OdometerDistance.Compute(readingsByVehicle[vehicleId]);

                if (distance.IgnoredReadings > 0)
                {
                    var plate = byPlate.Values.FirstOrDefault(v => v.Id == vehicleId)?.Plate ?? $"#{vehicleId}";
                    result.AddNote($"Véhicule « {plate} » : {distance.IgnoredReadings} relevé(s) compteur " +
                                   "incohérent(s) avec la série — à vérifier dans Carburant > Historique ou Réparations.");
                }
            }
        }

        _logger.LogInformation(
            "DataPort import société {CompanyId} : {V} véhicules, {M} entretiens, {R} réparations, {F} pleins créés",
            companyId, result.VehiclesCreated, result.MaintenanceCreated, result.RepairsCreated, result.FuelCreated);

        return Ok(result);
    }

    // ─────────────────────────────── Helpers ───────────────────────────────
    private static void WriteHeader(IXLWorksheet ws, string[] cols)
    {
        for (var i = 0; i < cols.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = cols[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
        }
    }

    private IActionResult WorkbookFile(XLWorkbook wb, string name)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    // Casse ET accents ignorés : une feuille nommée « Reparations » ou « Vehicules »
    // (clavier sans accents, fichier retapé dans un autre tableur) était ignorée en
    // silence par la comparaison stricte.
    private static IXLWorksheet? FindSheet(XLWorkbook wb, string name)
    {
        var key = RepairImportRow.NormalizeKey(name);
        return wb.Worksheets.FirstOrDefault(w => RepairImportRow.NormalizeKey(w.Name) == key);
    }

    // Lignes de données non vides (on saute l'en-tête et les lignes exemples en
    // italique du modèle sont, elles, réécrites par le client — on ne filtre que
    // le vide).
    private static IEnumerable<IXLRangeRow> DataRows(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used == null) yield break;
        var rows = used.RowsUsed().Skip(1); // saute l'en-tête
        foreach (var row in rows)
            if (!row.IsEmpty()) yield return row;
    }

    private static string Str(IXLCell c, string fallback = "") =>
        c.IsEmpty() ? fallback : (c.GetString().Trim() is { Length: > 0 } s ? s : fallback);

    private static int? Int(IXLCell c)
        => c.IsEmpty() ? null : (c.TryGetValue<double>(out var d) ? (int)Math.Round(d)
            : (int.TryParse(c.GetString().Trim(), out var i) ? i : null));

    // Une valeur hors de la plage du decimal (1E+30 tapé par erreur) faisait lever
    // une OverflowException et échouer tout l'import : elle est lue comme illisible.
    private static decimal? Dec(IXLCell c)
        => c.IsEmpty() ? null : (c.TryGetValue<double>(out var d) ? (Math.Abs(d) < 7.9e27 ? (decimal)d : (decimal?)null)
            : (decimal.TryParse(c.GetString().Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : null));

    /// <summary>Cellule remplie dont on ne sait pas lire le montant.</summary>
    private static bool Unreadable(IXLCell c) => !c.IsEmpty() && Dec(c) == null;

    private static DateTime? Date(IXLCell c)
    {
        if (c.IsEmpty()) return null;
        if (c.TryGetValue<DateTime>(out var dt)) return dt.Date;
        var s = c.GetString().Trim();
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" })
            if (DateTime.TryParseExact(s, fmt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var r)) return r.Date;
        return DateTime.TryParse(s, out var g) ? g.Date : null;
    }

    private static string Normalize(string plate) =>
        new string(plate.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public class ImportSummary
    {
        public int VehiclesCreated { get; set; }
        public int VehiclesUpdated { get; set; }
        public int VehiclesIgnored { get; set; }
        public int MaintenanceCreated { get; set; }
        public int MaintenanceIgnored { get; set; }
        public int RepairsCreated { get; set; }
        public int RepairsIgnored { get; set; }
        public int FuelCreated { get; set; }
        public int FuelIgnored { get; set; }
        public List<string> Notes { get; } = new();
        // On borne les notes pour ne pas renvoyer 10 000 lignes d'erreur.
        public void AddNote(string n) { if (Notes.Count < 50) Notes.Add(n); }
        public string Message =>
            $"{VehiclesCreated} véhicule(s), {MaintenanceCreated} entretien(s), {RepairsCreated} réparation(s) " +
            $"et {FuelCreated} plein(s) importés"
            + (VehiclesUpdated > 0 ? $", {VehiclesUpdated} kilométrage(s) mis à jour." : ".");
    }
}

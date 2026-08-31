using ClosedXML.Excel;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

/// <summary>
/// Import / export des données de la société au format Excel (recette client du
/// 25/08/2026). Trois feuilles : Véhicules, Entretiens, Carburant.
///
/// <para>Pensé d'abord pour l'offre « gestion de parc sans GPS » : démarrer un
/// parc en masse (import des véhicules) et récupérer ses données (export). Le
/// modèle téléchargeable a EXACTEMENT le format attendu par l'import — le client
/// exporte, complète, réimporte.</para>
///
/// <para>L'import ne fait que CRÉER : il n'écrase ni ne supprime rien. Un
/// véhicule dont le matricule existe déjà est ignoré (compté à part) ; un
/// entretien / plein dont le matricule est inconnu est ignoré et signalé. Le
/// résultat détaille ce qui a été créé et ce qui a été écarté, avec la raison.</para>
/// </summary>
[ApiController]
[Route("api/dataport")]
[Authorize]
public class DataPortController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ILogger<DataPortController> _logger;

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

        // Matricules déjà connus (dédup + résolution pour entretiens/pleins).
        var byPlate = await _context.Vehicles
            .Where(v => v.CompanyId == companyId && v.Plate != null)
            .ToDictionaryAsync(v => Normalize(v.Plate!), v => v);

        // 1) Véhicules
        var vs = FindSheet(wb, "Véhicules");
        if (vs != null)
        {
            foreach (var row in DataRows(vs))
            {
                var plate = Str(row.Cell(1));
                if (string.IsNullOrWhiteSpace(plate)) continue;
                var key = Normalize(plate);
                if (byPlate.ContainsKey(key)) { result.VehiclesIgnored++; result.AddNote($"Véhicule « {plate} » ignoré : matricule déjà présent."); continue; }

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
            // les entretiens et pleins par matricule.
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

        // 3) Carburant
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
                    OdometerKm = Int(row.Cell(6)) is int km && km > 0 ? km : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                result.FuelCreated++;
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "DataPort import société {CompanyId} : {V} véhicules, {M} entretiens, {F} pleins créés",
            companyId, result.VehiclesCreated, result.MaintenanceCreated, result.FuelCreated);

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

    private static IXLWorksheet? FindSheet(XLWorkbook wb, string name) =>
        wb.Worksheets.FirstOrDefault(w =>
            string.Equals(w.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

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

    private static decimal? Dec(IXLCell c)
        => c.IsEmpty() ? null : (c.TryGetValue<double>(out var d) ? (decimal)d
            : (decimal.TryParse(c.GetString().Trim().Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : null));

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
        public int VehiclesIgnored { get; set; }
        public int MaintenanceCreated { get; set; }
        public int MaintenanceIgnored { get; set; }
        public int FuelCreated { get; set; }
        public int FuelIgnored { get; set; }
        public List<string> Notes { get; } = new();
        // On borne les notes pour ne pas renvoyer 10 000 lignes d'erreur.
        public void AddNote(string n) { if (Notes.Count < 50) Notes.Add(n); }
        public string Message =>
            $"{VehiclesCreated} véhicule(s), {MaintenanceCreated} entretien(s) et {FuelCreated} plein(s) importés.";
    }
}

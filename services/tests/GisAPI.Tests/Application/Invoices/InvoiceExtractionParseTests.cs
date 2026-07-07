using FluentAssertions;
using GisAPI.Application.Services;
using Xunit;

namespace GisAPI.Tests.Application.Invoices;

/// <summary>
/// The LLM returns invoice fields in messy shapes (amounts as strings with FR
/// thousands/decimal separators, dates in dd/MM/yyyy, made-up categories). The
/// tolerant parser must coerce them so the review form is correctly pre-filled.
/// </summary>
public class InvoiceExtractionParseTests
{
    [Fact]
    public void Parses_stringified_amounts_fr_date_and_valid_category()
    {
        const string json = """
        {
          "supplierName": "Garage Ben Ali",
          "invoiceNumber": "F-2026-042",
          "date": "07/02/2026",
          "amountHT": "1000",
          "amountTVA": "234,560",
          "amountTTC": "1 234,560",
          "currency": "TND",
          "category": "maintenance",
          "vehiclePlate": "123 TU 4567",
          "description": "Vidange + filtres",
          "confidence": "high"
        }
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.SupplierName.Should().Be("Garage Ben Ali");
        r.InvoiceNumber.Should().Be("F-2026-042");
        r.Date.Should().Be("2026-02-07");                 // dd/MM/yyyy → ISO
        r.AmountHT.Should().Be(1000m);
        r.AmountTVA.Should().Be(234.560m);
        r.AmountTTC.Should().Be(1234.560m);               // "1 234,560" → 1234.56
        r.Category.Should().Be("maintenance");
        r.VehiclePlate.Should().Be("123 TU 4567");
        r.Confidence.Should().Be("high");
    }

    [Fact]
    public void Numeric_json_amounts_and_iso_date_pass_through()
    {
        const string json = """
        {"amountTTC": 85.5, "date": "2026-07-03", "category": "fuel"}
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.AmountTTC.Should().Be(85.5m);
        r.Date.Should().Be("2026-07-03");
        r.Category.Should().Be("fuel");
    }

    [Fact]
    public void Unknown_category_falls_back_to_other()
    {
        var r = InvoiceExtractionService.Parse("""{"category": "café-restaurant"}""");
        r.Category.Should().Be("other");
    }

    [Fact]
    public void Missing_or_empty_fields_become_null()
    {
        var r = InvoiceExtractionService.Parse("""{"supplierName": "", "amountTTC": null, "date": "pas une date"}""");
        r.SupplierName.Should().BeNull();
        r.AmountTTC.Should().BeNull();
        r.Date.Should().BeNull();
        r.Category.Should().Be("other");                  // no category → other
    }

    [Fact]
    public void Garbage_json_yields_low_confidence_empty_result_not_a_throw()
    {
        var r = InvoiceExtractionService.Parse("not json at all");
        r.Confidence.Should().Be("low");
        r.AmountTTC.Should().BeNull();
    }
}

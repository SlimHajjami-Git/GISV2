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

    [Fact]
    public void Items_are_parsed_with_string_amounts_and_bad_categories_normalized()
    {
        const string json = """
        {
          "amountTTC": 342.5,
          "category": "maintenance",
          "items": [
            { "label": "Vidange moteur", "amount": 120, "category": "maintenance" },
            { "label": "Filtre à huile", "amount": "82,500", "category": "pièces" },
            { "label": "Main d'œuvre", "amount": 140 },
            { "notALabel": true },
            "junk-string"
          ]
        }
        """;

        var r = InvoiceExtractionService.Parse(json);

        r.Items.Should().NotBeNull();
        r.Items!.Should().HaveCount(3);                    // junk entries skipped
        r.Items[0].Label.Should().Be("Vidange moteur");
        r.Items[0].Amount.Should().Be(120m);
        r.Items[0].Category.Should().Be("maintenance");
        r.Items[1].Amount.Should().Be(82.500m);            // "82,500" → 82.5
        r.Items[1].Category.Should().Be("other");          // "pièces" not in whitelist
        r.Items[2].Category.Should().BeNull();             // absent stays null (frontend falls back)
    }

    [Fact]
    public void Missing_items_array_yields_empty_list_and_alternative_keys_are_accepted()
    {
        InvoiceExtractionService.Parse("""{"amountTTC": 10}""")
            .Items.Should().BeEmpty();

        var r = InvoiceExtractionService.Parse("""
        {"items": [ { "designation": "Pneu 205/55R16", "total": "890" } ]}
        """);
        r.Items.Should().ContainSingle();
        r.Items![0].Label.Should().Be("Pneu 205/55R16");
        r.Items[0].Amount.Should().Be(890m);
    }
}

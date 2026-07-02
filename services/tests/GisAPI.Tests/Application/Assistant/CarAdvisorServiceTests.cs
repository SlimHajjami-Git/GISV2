using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Assistant;

/// <summary>
/// The car-buying advisor tools are the ONLY source of prices for the public
/// AI assistant (the system prompt forbids invented figures), so their JSON
/// contracts matter: found/not-found flags, budget filtering, and market-price
/// interpolation must behave exactly as the prompt assumes.
/// </summary>
public class CarAdvisorServiceTests
{
    private static CarMarketModel Picanto() => new()
    {
        Brand = "Kia", Model = "Picanto", Generation = "JA",
        YearFrom = 2017, YearTo = null, Segment = "populaire", Energy = "essence",
        EngineLabel = "1.2 MPI", FiscalPower = 4, Seats = 5,
        NewPriceTnd = 52900, RealConsumptionL100 = 5.8, ReliabilityScore = 8,
        BuyingAdvice = "Reine du segment populaire.",
        KnownIssuesJson = """[{"issue":"Embrayage urbain","severity":"moyenne"}]""",
        PartsPricesJson = """[{"part":"Plaquettes avant","priceTnd":140}]""",
        PriceCurveJson = """[{"year":2020,"km":60000,"priceTnd":38000},{"year":2022,"km":35000,"priceTnd":44000}]"""
    };

    private static CarMarketModel Hilux() => new()
    {
        Brand = "Toyota", Model = "Hilux", Generation = "AN120",
        YearFrom = 2016, YearTo = null, Segment = "pickup", Energy = "diesel",
        EngineLabel = "2.4 D-4D", FiscalPower = 9, Seats = 5,
        NewPriceTnd = 165000, RealConsumptionL100 = 9.5, ReliabilityScore = 10,
        BuyingAdvice = "Décote la plus faible du marché.",
        KnownIssuesJson = "[]",
        PartsPricesJson = "[]",
        PriceCurveJson = """[{"year":2020,"km":150000,"priceTnd":118000}]"""
    };

    private static async Task<CarAdvisorService> BuildAsync(TestGisDbContext ctx)
    {
        ctx.CarMarketModels.Add(Picanto());
        ctx.CarMarketModels.Add(Hilux());
        await ctx.SaveChangesAsync();
        return new CarAdvisorService(ctx);
    }

    private static async Task<JsonElement> CallAsync(CarAdvisorService svc, string tool, string args)
    {
        var json = await svc.ExecuteToolAsync(tool, args, CancellationToken.None);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Recommend_respects_budget_and_excludes_out_of_range_models()
    {
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        // 45 000 TND: Picanto occasion (38k/44k) fits; Hilux (118k occasion,
        // 165k neuf) must NOT appear.
        var r = await CallAsync(svc, "recommend_cars", """{"budget_tnd":45000}""");

        r.GetProperty("found").GetBoolean().Should().BeTrue();
        var candidates = r.GetProperty("candidats").EnumerateArray().ToList();
        candidates.Should().HaveCount(1, "only the Picanto fits a 45k budget");
        candidates[0].GetProperty("summary").GetProperty("model").GetString().Should().Be("Picanto");
        // The occasion option must be the newest curve point within budget (2022).
        candidates[0].GetProperty("options").GetProperty("occasion").GetProperty("Year").GetInt32().Should().Be(2022);
    }

    [Fact]
    public async Task Market_price_uses_nearest_curve_year_and_adjusts_for_mileage()
    {
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        // Reference 2020 = 38 000 @ 60 000 km. Asking with 100 000 km (40k over)
        // → -6% → 35 720 → rounded to hundreds = 35 700.
        var r = await CallAsync(svc, "get_market_price",
            """{"brand":"Kia","model":"Picanto","year":2020,"mileage_km":100000}""");

        r.GetProperty("found").GetBoolean().Should().BeTrue();
        r.GetProperty("estimationTnd").GetDecimal().Should().Be(35700);
        r.GetProperty("reference").GetProperty("prixMedianTnd").GetDecimal().Should().Be(38000);
    }

    [Fact]
    public async Task Unknown_model_returns_found_false_with_no_figures()
    {
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var r = await CallAsync(svc, "get_car_details", """{"brand":"Lada","model":"Niva"}""");

        r.GetProperty("found").GetBoolean().Should().BeFalse();
        r.GetProperty("message").GetString().Should().Contain("inventer",
            "the payload must explicitly instruct the model not to fabricate prices");
    }

    [Fact]
    public async Task Search_filters_by_segment_and_budget()
    {
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var bySegment = await CallAsync(svc, "search_car_models", """{"segment":"pickup"}""");
        bySegment.GetProperty("count").GetInt32().Should().Be(1);

        var byBudget = await CallAsync(svc, "search_car_models", """{"max_budget_tnd":50000}""");
        byBudget.GetProperty("count").GetInt32().Should().Be(1, "only the Picanto has an option under 50k");

        var byText = await CallAsync(svc, "search_car_models", """{"query":"toyota hilux"}""");
        byText.GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Resale_projects_depreciation_from_the_curve()
    {
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var r = await CallAsync(svc, "estimate_resale",
            """{"brand":"Kia","model":"Picanto","year":2022,"horizon_years":3}""");

        r.GetProperty("found").GetBoolean().Should().BeTrue();
        // Curve: 38k (2020) → 44k (2022) = ~6.8%/an, within the clamp [3%,15%].
        var rate = r.GetProperty("decoteAnnuellePct").GetDouble();
        rate.Should().BeInRange(3, 15);
        // Future value must be strictly below today's value.
        r.GetProperty("valeurDansNansTnd").GetDecimal().Should()
            .BeLessThan(r.GetProperty("valeurActuelleTnd").GetDecimal());
    }

    [Fact]
    public async Task Recommend_with_too_small_budget_returns_grounded_alternatives_above_budget()
    {
        // Real user scenario: "budget 30-35k" used to get a dry "nothing found".
        // The tool must return the nearest REAL options above budget so the
        // assistant can propose "à partir de 38 000 TND…" instead of refusing.
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var r = await CallAsync(svc, "recommend_cars", """{"budget_tnd":35000}""");

        r.GetProperty("found").GetBoolean().Should().BeFalse();
        var alts = r.GetProperty("alternativesAuDessusDuBudget").EnumerateArray().ToList();
        alts.Should().NotBeEmpty();
        // Cheapest above 35k in the test seed is the Picanto 2020 at 38 000.
        alts[0].GetProperty("prixTnd").GetDecimal().Should().Be(38000);
        alts[0].GetProperty("vehicule").GetString().Should().Contain("Picanto");
    }

    [Fact]
    public async Task Unknown_tool_returns_an_error_payload_instead_of_throwing()
    {
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var r = await CallAsync(svc, "does_not_exist", "{}");
        r.GetProperty("error").GetString().Should().Contain("does_not_exist");
    }

    [Fact]
    public async Task Numeric_args_sent_as_strings_are_accepted()
    {
        // LLMs routinely stringify numbers; a stringified budget must not
        // degrade into "Budget manquant".
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var r = await CallAsync(svc, "recommend_cars", """{"budget_tnd":"45000"}""");
        r.GetProperty("found").GetBoolean().Should().BeTrue();

        var p = await CallAsync(svc, "get_market_price",
            """{"brand":"Kia","model":"Picanto","year":"2020"}""");
        p.GetProperty("found").GetBoolean().Should().BeTrue();
        p.GetProperty("estimationTnd").GetDecimal().Should().Be(38000);
    }

    [Fact]
    public async Task Invalid_year_never_silently_picks_a_curve_point()
    {
        // A missing/unparseable year used to coalesce to 0 and select the
        // OLDEST curve point with found:true — a wrong price presented as real.
        using var ctx = TestDbContextFactory.Create();
        var svc = await BuildAsync(ctx);

        var r = await CallAsync(svc, "get_market_price",
            """{"brand":"Kia","model":"Picanto","year":"il ne sait pas"}""");
        r.GetProperty("found").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Generation_is_matchable_in_search_and_details()
    {
        // Users name cars by generation ("Golf 7", "Picanto JA") — the
        // generation column must participate in the fuzzy match.
        using var ctx = TestDbContextFactory.Create();
        ctx.CarMarketModels.Add(new CarMarketModel
        {
            Brand = "Volkswagen", Model = "Golf", Generation = "Golf 7",
            YearFrom = 2013, YearTo = 2020, Segment = "compacte", Energy = "essence",
            ReliabilityScore = 7, PriceCurveJson = """[{"year":2018,"km":110000,"priceTnd":72000}]"""
        });
        var svc = await BuildAsync(ctx);

        var d = await CallAsync(svc, "get_car_details", """{"brand":"Volkswagen","model":"Golf 7"}""");
        d.GetProperty("found").GetBoolean().Should().BeTrue();

        var s = await CallAsync(svc, "search_car_models", """{"query":"picanto ja"}""");
        s.GetProperty("count").GetInt32().Should().Be(1);
    }
}

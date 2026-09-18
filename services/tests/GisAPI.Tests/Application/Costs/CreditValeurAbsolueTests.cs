using FluentAssertions;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Costs;

/// <summary>
/// Avant le refus des montants négatifs (DEF-050), un avoir pouvait être saisi à −120.
/// Avec le signe −1 des crédits, −1 × −120 en faisait une DÉPENSE de +120 : un écart de
/// deux fois le montant. Un crédit se déduit toujours en valeur absolue.
/// </summary>
public class CreditValeurAbsolueTests
{
    [Theory]
    [InlineData("credit_note", 120, -120)]
    [InlineData("credit_note", -120, -120)]
    [InlineData("avoir", -120, -120)]
    [InlineData("insurance_refund", -80, -80)]
    [InlineData("fuel", 50, 50)]
    [InlineData("fuel", -50, -50)]
    public void Un_credit_se_deduit_en_valeur_absolue(string type, decimal amount, decimal attendu)
        => VehicleCostCategory.SignedAmount(type, amount).Should().Be(attendu);

    [Fact]
    public async Task Le_total_signe_deduit_chaque_credit_en_valeur_absolue_meme_dans_un_type_melange()
    {
        await using var ctx = TestDbContextFactory.Create();
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, VehicleId = 1, CompanyId = 1, Type = "fuel", Amount = 500m, Date = new DateTime(2026, 9, 1) },
            new VehicleCost { Id = 2, VehicleId = 1, CompanyId = 1, Type = "avoir", Amount = -120m, Date = new DateTime(2026, 9, 2) },
            new VehicleCost { Id = 3, VehicleId = 1, CompanyId = 1, Type = "avoir", Amount = 30m, Date = new DateTime(2026, 9, 3) });
        await ctx.SaveChangesAsync();

        // 500 − 120 − 30 ; une somme brute du type « avoir » (−90) aurait donné 590.
        (await VehicleCostCategory.SignedTotalAsync(ctx.VehicleCosts)).Should().Be(350m);
    }
}

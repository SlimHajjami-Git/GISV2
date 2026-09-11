using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Quota mensuel de scans de factures IA, sur le VRAI <see cref="CostsController"/>.
///
/// Recette du 11/09/2026 : « chaque mois ce quota se réinitialise ». Le serveur le
/// faisait déjà, mais l'écran affichait le RESTE (« 16/20 ») sans date de remise à
/// zéro : le client lisait 16 scans consommés et croyait le compteur cumulé sans fin.
/// Ces tests fixent la règle (mois civil, par société) et ce que l'écran reçoit.
/// </summary>
public class ScanQuotaMonthlyTests
{
    private const int CompanyId = 14;
    private const int OtherCompanyId = 10;

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime MonthStart = new(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CostsController Controller(TestGisDbContext ctx)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns(45);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);

        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new CostsController(ctx, tenant.Object, publisher.Object,
            new Mock<IInvoiceExtractionService>().Object, new Mock<IWebHostEnvironment>().Object, new Mock<ILogger<CostsController>>().Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("companyId", CompanyId.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, "45"),
                }, "test"))
            }
        };
        return controller;
    }

    private static JsonElement Body(IActionResult result)
    {
        var value = result switch
        {
            ObjectResult o => o.Value,
            _ => throw new InvalidOperationException("réponse inattendue " + result.GetType().Name),
        };
        return JsonSerializer.SerializeToElement(value);
    }

    private static async Task<TestGisDbContext> SeedAsync(int? limit, params (int Company, DateTime At)[] scans)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe { Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true, InvoiceScanMonthlyLimit = limit });
        ctx.Societes.Add(new Societe { Id = OtherCompanyId, Name = "SICOAC", SubscriptionStatus = "active", IsActive = true });
        foreach (var (company, at) in scans)
            ctx.InvoiceScanLogs.Add(new InvoiceScanLog { CompanyId = company, UserId = 45, TokensUsed = 2700, CreatedAt = at });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Le_compteur_ne_voit_que_les_scans_du_mois_civil_en_cours()
    {
        using var ctx = await SeedAsync(null,
            (CompanyId, MonthStart.AddDays(-1)),        // mois précédent : ne compte plus
            (CompanyId, MonthStart.AddSeconds(-1)),     // dernière seconde du mois précédent
            (CompanyId, MonthStart),                    // première seconde du mois : compte
            (CompanyId, Now),
            (OtherCompanyId, Now));                     // une autre société : jamais

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("used").GetInt32().Should().Be(2);
        body.GetProperty("limit").GetInt32().Should().Be(20, "20 par défaut quand la société n'a pas de limite propre");
        body.GetProperty("remaining").GetInt32().Should().Be(18);
    }

    [Fact]
    public async Task L_ecran_recoit_la_date_de_remise_a_zero()
    {
        using var ctx = await SeedAsync(null, (CompanyId, Now));

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("resetsAt").GetDateTime().Should().Be(MonthStart.AddMonths(1), "le compteur repart à zéro le 1er du mois suivant");
    }

    [Fact]
    public async Task Quota_atteint_le_message_donne_la_date_de_remise_a_zero()
    {
        using var ctx = await SeedAsync(1, (CompanyId, Now));
        var file = new FormFile(new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }), 0, 3, "file", "facture.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

        var result = await Controller(ctx).ScanInvoice(file, CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        var body = Body(result);
        body.GetProperty("message").GetString().Should().Contain(MonthStart.AddMonths(1).ToString("dd/MM/yyyy"));
        body.GetProperty("resetsAt").GetDateTime().Should().Be(MonthStart.AddMonths(1));
    }
}

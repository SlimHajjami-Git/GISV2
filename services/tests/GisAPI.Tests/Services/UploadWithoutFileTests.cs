using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Campagne de test GPA, DEF-056 : un envoi sans pièce jointe sur les routes d'upload
/// recevait le message de liaison ASP.NET en anglais (« The file field is required. »)
/// au lieu de « Aucun fichier reçu. ». Cause : le paramètre IFormFile était déclaré
/// NON nullable ; avec les types référence nullables, MVC lui ajoute un [Required]
/// implicite et [ApiController] rejette la requête avant l'action, dont le contrôle
/// métier devenait inatteignable.
///
/// <para>Le test interroge le fournisseur de métadonnées de MVC, celui-là même qui
/// décide du [Required] implicite : un paramètre redevenu non nullable le fait échouer.</para>
/// </summary>
public class UploadWithoutFileTests
{
    private static ModelMetadataProvider MetadataProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore().AddDataAnnotations();
        return (ModelMetadataProvider)services.BuildServiceProvider().GetRequiredService<IModelMetadataProvider>();
    }

    private static ModelMetadata FileParameter(Type controller, string action)
    {
        var parameter = controller.GetMethod(action)!.GetParameters().Single(p => p.ParameterType == typeof(IFormFile));
        return MetadataProvider().GetMetadataForParameter(parameter);
    }

    /// <summary>Témoin : un IFormFile non nullable reçoit bien le [Required] implicite.</summary>
    private sealed class Temoin
    {
        public void Envoyer(IFormFile file) { }
    }

    [Fact]
    public void Temoin_un_parametre_non_nullable_est_rejete_par_MVC_avant_l_action()
    {
        var metadata = FileParameter(typeof(Temoin), nameof(Temoin.Envoyer));

        metadata.IsRequired.Should().BeTrue("sinon ce test ne détecterait pas la cause du défaut");
        metadata.ValidatorMetadata.OfType<RequiredAttribute>().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(typeof(AcquisitionPaymentsController), nameof(AcquisitionPaymentsController.UploadReceipt))]
    [InlineData(typeof(CostsController), nameof(CostsController.ScanInvoice))]
    [InlineData(typeof(DataPortController), nameof(DataPortController.Import))]
    // Même cause, relevée au tour d'intégration : rapport PDF et pièces du dossier
    // sinistre, import Excel des prix carburant.
    [InlineData(typeof(AccidentReportsController), nameof(AccidentReportsController.UploadPdf))]
    [InlineData(typeof(AccidentReportsController), nameof(AccidentReportsController.AddDocument))]
    [InlineData(typeof(FuelPricesController), nameof(FuelPricesController.ImportFuelPrices))]
    public void Sans_fichier_la_requete_atteint_l_action(Type controller, string action)
    {
        var metadata = FileParameter(controller, action);

        metadata.IsRequired.Should().BeFalse();
        metadata.ValidatorMetadata.OfType<RequiredAttribute>().Should().BeEmpty();
    }

    // Message métier : ces appels directs contournent la liaison MVC, dont la cause est
    // couverte par la théorie ci-dessus. Quittance et scan passaient déjà avant le
    // correctif ; dossier sinistre (« Fichier requis ») et prix carburant (chaîne
    // anglaise sans propriété message) échouaient.
    private static string Message(IActionResult result) =>
        JsonSerializer.SerializeToElement(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value)
            .GetProperty("message").GetString()!;

    [Fact]
    public async Task Quittance_d_echeance_sans_fichier_message_metier()
    {
        var controller = new AcquisitionPaymentsController(
            Mock.Of<IMediator>(), TestDbContextFactory.CreateMockTenantService(7).Object,
            Mock.Of<IWebHostEnvironment>(), Mock.Of<IDashboardCache>(),
            NullLogger<AcquisitionPaymentsController>.Instance);

        Message(await controller.UploadReceipt(29, null, CancellationToken.None)).Should().Be("Aucun fichier reçu.");
    }

    [Fact]
    public async Task Scan_de_facture_sans_fichier_message_metier()
    {
        using var ctx = TestDbContextFactory.Create();
        var controller = new CostsController(
            ctx, TestDbContextFactory.CreateMockTenantService(7).Object, Mock.Of<IPublisher>(),
            Mock.Of<IInvoiceExtractionService>(), Mock.Of<IWebHostEnvironment>(), Mock.Of<ILogger<CostsController>>());

        Message(await controller.ScanInvoice(null, CancellationToken.None)).Should().Be("Aucun fichier reçu.");
    }

    [Fact]
    public async Task Dossier_sinistre_sans_fichier_message_metier()
    {
        // Le contrôle du fichier précède toute lecture : ni base ni société sollicitées.
        var controller = new AccidentReportsController(
            Mock.Of<IMediator>(), null!, Mock.Of<INotificationService>(),
            TestDbContextFactory.CreateMockTenantService(7).Object, Mock.Of<IWebHostEnvironment>(),
            Mock.Of<IManualAccidentEnricher>(), NullLogger<AccidentReportsController>.Instance);

        Message((await controller.UploadPdf(12, null, CancellationToken.None)).Result!).Should().Be("Aucun fichier reçu.");
        Message((await controller.AddDocument(12, null, "photo", CancellationToken.None)).Result!).Should().Be("Aucun fichier reçu.");
    }

    [Fact]
    public async Task Import_des_prix_carburant_sans_fichier_message_metier()
    {
        var controller = new FuelPricesController(Mock.Of<IMediator>(), NullLogger<FuelPricesController>.Instance);

        Message((await controller.ImportFuelPrices(null)).Result!).Should().Be("Aucun fichier reçu.");

        // Même action, même forme de réponse pour un format refusé (chaîne anglaise brute avant).
        var csv = new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "file", "prix.csv");
        Message((await controller.ImportFuelPrices(csv)).Result!)
            .Should().Be("Format non supporté : envoyez un fichier Excel (.xlsx ou .xls).");
    }
}

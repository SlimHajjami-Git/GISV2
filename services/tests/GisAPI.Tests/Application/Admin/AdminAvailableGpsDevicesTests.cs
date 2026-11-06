using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Admin.GpsDevices;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Point 4 des corrections restantes (06/11/2026) : dans la console, la fiche véhicule ne
/// proposait aucun boîtier disponible (elle appelait la route de l'espace client, refusée faute
/// de clé client). La route admin donne les boîtiers libres de la société DU VÉHICULE.
/// </summary>
public class AdminAvailableGpsDevicesTests
{
    [Fact]
    public async Task Seuls_les_boitiers_libres_de_la_societe_du_vehicule_sont_proposes()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Societes.AddRange(
            new Societe { Id = 1, Name = "BELIVE", SubscriptionStatus = "active", IsActive = true },
            new Societe { Id = 4, Name = "HERTZ", SubscriptionStatus = "active", IsActive = true });
        ctx.GpsDevices.AddRange(
            // Libre, de la société du véhicule : proposé, avec ce que la fiche recopie.
            new GpsDevice { Id = 10, DeviceUid = "860000000000101", Mat = "NR1", CompanyId = 4, Status = "unassigned",
                            SimNumber = "21600000", SimOperator = "Ooredoo", FuelSensorMode = "raw_100" },
            // Déjà posé sur un véhicule.
            new GpsDevice { Id = 11, DeviceUid = "860000000000102", CompanyId = 4, Status = "assigned" },
            // Libre, mais d'une autre société : l'enregistrement le refuserait.
            new GpsDevice { Id = 12, DeviceUid = "860000000000103", CompanyId = 1, Status = "unassigned" },
            // Statut « unassigned » mais rattaché à un véhicule : pas libre.
            new GpsDevice { Id = 13, DeviceUid = "860000000000104", CompanyId = 4, Status = "unassigned" },
            // Fiche « fantôme » créée par l'ingestion (IMEI collé à l'écho d'une commande d'heure) :
            // libre en apparence, mais ce n'est pas un boîtier.
            new GpsDevice { Id = 14, DeviceUid = "860000000000101at+cclk=\"26/10/25", CompanyId = 4, Status = "unassigned" },
            // Identifiant par MAT (lettres et chiffres) : un vrai boîtier, proposé.
            new GpsDevice { Id = 15, DeviceUid = "NR09G12193", CompanyId = 4, Status = "unassigned" });
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 20, Name = "HTZ 1", Plate = "HTZ 1", Brand = "Seat", Model = "Ibiza", Type = "citadine", Status = "available", CompanyId = 4, GpsDeviceId = 11 },
            new Vehicle { Id = 21, Name = "HTZ 2", Plate = "HTZ 2", Brand = "Seat", Model = "Ibiza", Type = "citadine", Status = "available", CompanyId = 4, GpsDeviceId = 13 });
        await ctx.SaveChangesAsync();

        var libres = await new GetAdminAvailableGpsDevicesQueryHandler(ctx)
            .Handle(new GetAdminAvailableGpsDevicesQuery(4), CancellationToken.None);

        libres.Select(d => d.Id).Should().BeEquivalentTo(new[] { 10, 15 });
        var boitier = libres.Single(d => d.Id == 10);
        boitier.DeviceUid.Should().Be("860000000000101");
        boitier.Mat.Should().Be("NR1");
        boitier.SimNumber.Should().Be("21600000");
        boitier.SimOperator.Should().Be("Ooredoo");
        boitier.FuelSensorMode.Should().Be("raw_100");
    }

    [Fact]
    public void Le_compte_restreint_avec_l_ecran_Vehicules_peut_lire_la_liste_mais_pas_l_ecran_Boitiers_GPS()
    {
        var vehicules = new[] { AdminScreenAccess.Vehicles };
        AdminScreenAccess.CanCall(vehicules, "GET", "/api/admin/gps-devices/available").Should().BeTrue();
        AdminScreenAccess.CanCall(vehicules, "GET", "/api/admin/gps-devices").Should().BeFalse();
        AdminScreenAccess.CanCall(new[] { AdminScreenAccess.Monitoring }, "GET", "/api/admin/gps-devices/available").Should().BeFalse();
    }
}

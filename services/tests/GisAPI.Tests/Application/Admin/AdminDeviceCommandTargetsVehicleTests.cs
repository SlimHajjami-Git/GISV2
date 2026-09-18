using FluentAssertions;
using GisAPI.Application.Features.Admin.DeviceCommands;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Écran admin « Commandes boîtiers » (demande du 18/09/2026) : chaque ligne porte la marque et le
/// modèle du VÉHICULE, distincts de ceux du boîtier (Brand/Model = « NEMS » / « L »).
/// </summary>
public class AdminDeviceCommandTargetsVehicleTests
{
    [Fact]
    public async Task Chaque_ligne_porte_la_marque_et_le_modele_du_vehicule_distincts_de_ceux_du_boitier()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe { Id = 4, Name = "HERTZ", SubscriptionStatus = "active", IsActive = true });
        var withVehicle = new GpsDevice { Id = 10, DeviceUid = "860141076678086", Mat = "NR08G0881", Brand = "NEMS", Model = "L", CompanyId = 4, Status = "assigned" };
        var alone = new GpsDevice { Id = 11, DeviceUid = "860141076675892", Brand = "NEMS", Model = "L", CompanyId = 4, Status = "unassigned" };
        ctx.GpsDevices.AddRange(withVehicle, alone);
        ctx.Vehicles.Add(new Vehicle { Id = 20, Name = "233 TU 5102", Plate = "233 TU 5102", Brand = "Seat", Model = "Ibiza", Type = "citadine", Status = "available", CompanyId = 4, GpsDeviceId = 10 });
        await ctx.SaveChangesAsync();

        var rows = await new GetAdminDeviceCommandTargetsQueryHandler(ctx)
            .Handle(new GetAdminDeviceCommandTargetsQuery(4), CancellationToken.None);

        var equipped = rows.Single(r => r.DeviceId == 10);
        equipped.VehicleBrand.Should().Be("Seat");
        equipped.VehicleModel.Should().Be("Ibiza");
        equipped.Brand.Should().Be("NEMS", "la marque du boîtier reste distincte");
        equipped.Model.Should().Be("L");

        var bare = rows.Single(r => r.DeviceId == 11);
        bare.VehicleBrand.Should().BeNull("un boîtier sans véhicule n'a ni marque ni modèle de véhicule");
        bare.VehicleModel.Should().BeNull();
    }
}

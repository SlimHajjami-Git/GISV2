using FluentAssertions;
using GisAPI.Domain.Entities;

namespace GisAPI.Tests.Domain;

public class SocieteTests
{
    [Fact]
    public void Societe_ShouldInitializeWithDefaultValues()
    {
        // Act
        var societe = new Societe();

        // Assert
        societe.Id.Should().Be(0);
        societe.Name.Should().BeEmpty();
        societe.Type.Should().Be("transport");
        societe.Country.Should().Be("TN");
        societe.IsActive.Should().BeTrue();
        societe.BillingCycle.Should().Be("yearly");
        societe.SubscriptionStatus.Should().Be("active");
        societe.Users.Should().NotBeNull().And.BeEmpty();
        societe.Vehicles.Should().NotBeNull().And.BeEmpty();
        societe.Geofences.Should().NotBeNull().And.BeEmpty();
        societe.GpsDevices.Should().NotBeNull().And.BeEmpty();
        societe.Roles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Societe_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var societe = new Societe
        {
            Id = 1,
            Name = "Test Company",
            Type = "logistics",
            Address = "123 Test Street",
            City = "Tunis",
            Country = "TN",
            Phone = "+216 12 345 678",
            Email = "contact@test.com",
            IsActive = true,
            SubscriptionTypeId = 1
        };

        // Assert
        societe.Id.Should().Be(1);
        societe.Name.Should().Be("Test Company");
        societe.Type.Should().Be("logistics");
        societe.Address.Should().Be("123 Test Street");
        societe.City.Should().Be("Tunis");
        societe.Country.Should().Be("TN");
        societe.Phone.Should().Be("+216 12 345 678");
        societe.Email.Should().Be("contact@test.com");
        societe.IsActive.Should().BeTrue();
        societe.SubscriptionTypeId.Should().Be(1);
    }

    [Fact]
    public void Societe_ShouldHaveSettings()
    {
        // Arrange
        var societe = new Societe
        {
            Settings = new SocieteSettings
            {
                Currency = "EUR",
                Timezone = "Europe/Paris",
                Language = "fr"
            }
        };

        // Assert
        societe.Settings.Should().NotBeNull();
        societe.Settings!.Currency.Should().Be("EUR");
        societe.Settings.Timezone.Should().Be("Europe/Paris");
        societe.Settings.Language.Should().Be("fr");
    }

    [Fact]
    public void SocieteSettings_ShouldHaveDefaultValues()
    {
        // Act
        var settings = new SocieteSettings();

        // Assert
        settings.Currency.Should().Be("DT");
        settings.Timezone.Should().Be("Africa/Tunis");
        settings.Language.Should().Be("fr");
        settings.DateFormat.Should().Be("dd/MM/yyyy");
        settings.DistanceUnit.Should().Be("km");
        settings.SpeedUnit.Should().Be("kmh");
        settings.VolumeUnit.Should().Be("L");
    }
}

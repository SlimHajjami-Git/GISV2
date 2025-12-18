using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using GisAPI.Controllers;
using GisAPI.Data;
using GisAPI.Models;
using GisAPI.DTOs;

namespace GisAPI.Tests.Controllers;

public class VehiclesControllerTests
{
    private GisDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new GisDbContext(options);
    }

    private VehiclesController CreateController(GisDbContext context, int companyId = 1)
    {
        var controller = new VehiclesController(context);
        
        var claims = new List<Claim>
        {
            new Claim("companyId", companyId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "1")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        
        return controller;
    }

    [Fact]
    public async Task GetVehicles_ReturnsEmptyList_WhenNoVehicles()
    {
        // Arrange
        using var context = CreateContext();
        var controller = CreateController(context);

        // Act
        var result = await controller.GetVehicles();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var vehicles = Assert.IsType<List<VehicleDto>>(okResult.Value);
        Assert.Empty(vehicles);
    }

    [Fact]
    public async Task GetVehicles_ReturnsVehicles_ForCompany()
    {
        // Arrange
        using var context = CreateContext();
        context.Vehicles.Add(new Vehicle
        {
            Id = 1,
            Name = "Test Vehicle",
            Type = "camion",
            Brand = "Mercedes",
            Model = "Actros",
            Plate = "ABC-123",
            Year = 2024,
            CompanyId = 1,
            Status = "available"
        });
        context.Vehicles.Add(new Vehicle
        {
            Id = 2,
            Name = "Other Company Vehicle",
            Type = "camion",
            Brand = "Volvo",
            Model = "FH",
            Plate = "XYZ-999",
            Year = 2023,
            CompanyId = 2, // Different company
            Status = "available"
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.GetVehicles();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var vehicles = Assert.IsType<List<VehicleDto>>(okResult.Value);
        Assert.Single(vehicles);
        Assert.Equal("Test Vehicle", vehicles[0].Name);
    }

    [Fact]
    public async Task GetVehicle_ReturnsNotFound_WhenVehicleDoesNotExist()
    {
        // Arrange
        using var context = CreateContext();
        var controller = CreateController(context);

        // Act
        var result = await controller.GetVehicle(999);

        // Assert
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetVehicle_ReturnsVehicle_WhenExists()
    {
        // Arrange
        using var context = CreateContext();
        context.Vehicles.Add(new Vehicle
        {
            Id = 1,
            Name = "Test Vehicle",
            Type = "camion",
            Brand = "Mercedes",
            Model = "Actros",
            Plate = "ABC-123",
            Year = 2024,
            CompanyId = 1,
            Status = "available"
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.GetVehicle(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var vehicle = Assert.IsType<VehicleDto>(okResult.Value);
        Assert.Equal("Test Vehicle", vehicle.Name);
    }

    [Fact]
    public async Task CreateVehicle_ReturnsCreatedVehicle()
    {
        // Arrange
        using var context = CreateContext();
        var controller = CreateController(context);
        var request = new CreateVehicleRequest(
            Name: "New Vehicle",
            Type: "camion",
            Brand: "Mercedes",
            Model: "Actros",
            Plate: "NEW-123",
            Year: 2024,
            Color: "White",
            Mileage: 0
        );

        // Act
        var result = await controller.CreateVehicle(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var vehicle = Assert.IsType<VehicleDto>(createdResult.Value);
        Assert.Equal("New Vehicle", vehicle.Name);
        Assert.Equal("available", vehicle.Status);
    }

    [Fact]
    public async Task DeleteVehicle_ReturnsNoContent_WhenDeleted()
    {
        // Arrange
        using var context = CreateContext();
        context.Vehicles.Add(new Vehicle
        {
            Id = 1,
            Name = "To Delete",
            Type = "camion",
            Brand = "Mercedes",
            Model = "Actros",
            Plate = "DEL-123",
            Year = 2024,
            CompanyId = 1,
            Status = "available"
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.DeleteVehicle(1);

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Empty(context.Vehicles);
    }

    [Fact]
    public async Task DeleteVehicle_ReturnsNotFound_WhenVehicleDoesNotExist()
    {
        // Arrange
        using var context = CreateContext();
        var controller = CreateController(context);

        // Act
        var result = await controller.DeleteVehicle(999);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}

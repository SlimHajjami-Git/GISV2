using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using GisAPI.Controllers;
using GisAPI.Data;
using GisAPI.Models;

namespace GisAPI.Tests.Controllers;

public class EmployeesControllerTests
{
    private GisDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new GisDbContext(options);
    }

    private EmployeesController CreateController(GisDbContext context, int companyId = 1)
    {
        var controller = new EmployeesController(context);
        
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
    public async Task GetEmployees_ReturnsEmptyList_WhenNoEmployees()
    {
        // Arrange
        using var context = CreateContext();
        var controller = CreateController(context);

        // Act
        var result = await controller.GetEmployees();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var employees = Assert.IsType<List<Employee>>(okResult.Value);
        Assert.Empty(employees);
    }

    [Fact]
    public async Task GetEmployees_ReturnsEmployees_ForCompany()
    {
        // Arrange
        using var context = CreateContext();
        context.Employees.Add(new Employee
        {
            Id = 1,
            Name = "Test Driver",
            Email = "driver@test.com",
            Phone = "+212600000000",
            Role = "driver",
            Status = "active",
            CompanyId = 1
        });
        context.Employees.Add(new Employee
        {
            Id = 2,
            Name = "Other Company Driver",
            Email = "other@test.com",
            Phone = "+212600000001",
            Role = "driver",
            Status = "active",
            CompanyId = 2 // Different company
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.GetEmployees();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var employees = Assert.IsType<List<Employee>>(okResult.Value);
        Assert.Single(employees);
        Assert.Equal("Test Driver", employees[0].Name);
    }

    [Fact]
    public async Task GetDrivers_ReturnsOnlyDrivers()
    {
        // Arrange
        using var context = CreateContext();
        context.Employees.Add(new Employee
        {
            Id = 1,
            Name = "Driver One",
            Email = "driver1@test.com",
            Role = "driver",
            Status = "active",
            CompanyId = 1
        });
        context.Employees.Add(new Employee
        {
            Id = 2,
            Name = "Supervisor One",
            Email = "supervisor@test.com",
            Role = "supervisor",
            Status = "active",
            CompanyId = 1
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.GetDrivers();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var drivers = Assert.IsType<List<Employee>>(okResult.Value);
        Assert.Single(drivers);
        Assert.Equal("Driver One", drivers[0].Name);
    }

    [Fact]
    public async Task GetSupervisors_ReturnsOnlySupervisors()
    {
        // Arrange
        using var context = CreateContext();
        context.Employees.Add(new Employee
        {
            Id = 1,
            Name = "Driver One",
            Email = "driver1@test.com",
            Role = "driver",
            Status = "active",
            CompanyId = 1
        });
        context.Employees.Add(new Employee
        {
            Id = 2,
            Name = "Supervisor One",
            Email = "supervisor@test.com",
            Role = "supervisor",
            Status = "active",
            CompanyId = 1
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.GetSupervisors();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var supervisors = Assert.IsType<List<Employee>>(okResult.Value);
        Assert.Single(supervisors);
        Assert.Equal("Supervisor One", supervisors[0].Name);
    }

    [Fact]
    public async Task CreateEmployee_ReturnsCreatedEmployee()
    {
        // Arrange
        using var context = CreateContext();
        var controller = CreateController(context);
        var employee = new Employee
        {
            Name = "New Driver",
            Email = "newdriver@test.com",
            Phone = "+212600000000",
            Role = "driver",
            Status = "active"
        };

        // Act
        var result = await controller.CreateEmployee(employee);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var created = Assert.IsType<Employee>(createdResult.Value);
        Assert.Equal("New Driver", created.Name);
        Assert.Equal(1, created.CompanyId);
    }

    [Fact]
    public async Task DeleteEmployee_ReturnsNoContent_WhenDeleted()
    {
        // Arrange
        using var context = CreateContext();
        context.Employees.Add(new Employee
        {
            Id = 1,
            Name = "To Delete",
            Email = "delete@test.com",
            Role = "driver",
            Status = "active",
            CompanyId = 1
        });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context, companyId: 1);

        // Act
        var result = await controller.DeleteEmployee(1);

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Empty(context.Employees);
    }
}

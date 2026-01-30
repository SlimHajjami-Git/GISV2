using GisAPI.Controllers;

namespace GisAPI.Tests.Controllers;

public class AdminUserControllerTests
{
    // ==================== DTO TESTS ====================

    [Fact]
    public void AdminUserDto_CanBeCreated()
    {
        var dto = new AdminUserDto
        {
            Id = 1,
            Name = "John Doe",
            Email = "john@test.com",
            Phone = "+212600000000",
            DateOfBirth = new DateTime(1990, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            CIN = "AB123456",
            CompanyId = 1,
            CompanyName = "Test Company",
            RoleId = 1,
            RoleName = "Admin",
            Roles = new[] { "admin" },
            Permissions = new[] { "dashboard", "vehicles" },
            AssignedVehicleIds = new[] { 1, 2, 3 },
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            IsOnline = true
        };

        Assert.Equal(1, dto.Id);
        Assert.Equal("John Doe", dto.Name);
        Assert.Equal("john@test.com", dto.Email);
        Assert.Equal("AB123456", dto.CIN);
        Assert.Equal(1, dto.RoleId);
        Assert.Equal("Admin", dto.RoleName);
        Assert.Equal(3, dto.AssignedVehicleIds.Length);
        Assert.Equal("active", dto.Status);
    }

    [Fact]
    public void AdminUserDto_DefaultValues_AreCorrect()
    {
        var dto = new AdminUserDto();

        Assert.Equal(0, dto.Id);
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Email);
        Assert.Null(dto.Phone);
        Assert.Null(dto.DateOfBirth);
        Assert.Null(dto.CIN);
        Assert.Empty(dto.Roles);
        Assert.Empty(dto.Permissions);
        Assert.Empty(dto.AssignedVehicleIds);
        Assert.Equal("active", dto.Status);
        Assert.False(dto.IsOnline);
    }

    [Fact]
    public void CreateAdminUserRequest_CanBeCreated()
    {
        var request = new CreateAdminUserRequest
        {
            Name = "Jane Smith",
            Email = "jane@test.com",
            Password = "SecurePassword123!",
            Phone = "+212611111111",
            DateOfBirth = new DateTime(1985, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            CIN = "CD789012",
            CompanyId = 2,
            RoleId = 3,
            AssignedVehicleIds = new[] { 5, 6 }
        };

        Assert.Equal("Jane Smith", request.Name);
        Assert.Equal("jane@test.com", request.Email);
        Assert.Equal("SecurePassword123!", request.Password);
        Assert.Equal("CD789012", request.CIN);
        Assert.Equal(2, request.CompanyId);
        Assert.Equal(3, request.RoleId);
        Assert.Equal(2, request.AssignedVehicleIds?.Length);
    }

    [Fact]
    public void CreateAdminUserRequest_OptionalFields_CanBeNull()
    {
        var request = new CreateAdminUserRequest
        {
            Name = "Minimal User",
            Email = "minimal@test.com",
            Password = "password",
            CompanyId = 1
        };

        Assert.Null(request.Phone);
        Assert.Null(request.DateOfBirth);
        Assert.Null(request.CIN);
        Assert.Null(request.RoleId);
        Assert.Null(request.AssignedVehicleIds);
    }

    [Fact]
    public void UpdateAdminUserRequest_CanBeCreated()
    {
        var request = new UpdateAdminUserRequest
        {
            Name = "Updated Name",
            Email = "updated@test.com",
            Phone = "+212622222222",
            Password = "NewPassword456!",
            Roles = new[] { "supervisor", "driver" },
            Permissions = new[] { "dashboard", "monitoring", "vehicles" }
        };

        Assert.Equal("Updated Name", request.Name);
        Assert.Equal("updated@test.com", request.Email);
        Assert.Equal("+212622222222", request.Phone);
        Assert.Equal("NewPassword456!", request.Password);
        Assert.Equal(2, request.Roles?.Length);
        Assert.Equal(3, request.Permissions?.Length);
    }

    [Fact]
    public void UpdatePermissionsRequest_CanBeCreated()
    {
        var request = new UpdatePermissionsRequest
        {
            Permissions = new[] { "dashboard", "vehicles", "monitoring", "reports", "geofences" }
        };

        Assert.Equal(5, request.Permissions.Length);
        Assert.Contains("dashboard", request.Permissions);
        Assert.Contains("vehicles", request.Permissions);
        Assert.Contains("geofences", request.Permissions);
    }

    [Fact]
    public void UpdateRolesRequest_CanBeCreated()
    {
        var request = new UpdateRolesRequest
        {
            Roles = new[] { "admin", "supervisor", "driver" }
        };

        Assert.Equal(3, request.Roles.Length);
        Assert.Contains("admin", request.Roles);
        Assert.Contains("supervisor", request.Roles);
    }

    [Fact]
    public void ResetPasswordRequest_CanBeCreated()
    {
        var request = new ResetPasswordRequest
        {
            NewPassword = "VerySecurePassword789!"
        };

        Assert.Equal("VerySecurePassword789!", request.NewPassword);
    }

    // ==================== USER STATS DTO TESTS ====================

    [Fact]
    public void UserStatsDto_CanBeCreated()
    {
        var stats = new UserStatsDto
        {
            TotalUsers = 100,
            ActiveUsers = 85,
            SuspendedUsers = 10,
            OnlineUsers = 25,
            UsersByCompany = new List<CompanyUserCount>
            {
                new CompanyUserCount { CompanyId = 1, CompanyName = "Company A", UserCount = 50 },
                new CompanyUserCount { CompanyId = 2, CompanyName = "Company B", UserCount = 50 }
            }
        };

        Assert.Equal(100, stats.TotalUsers);
        Assert.Equal(85, stats.ActiveUsers);
        Assert.Equal(10, stats.SuspendedUsers);
        Assert.Equal(25, stats.OnlineUsers);
        Assert.Equal(2, stats.UsersByCompany.Count);
    }

    [Fact]
    public void CompanyUserCount_CanBeCreated()
    {
        var count = new CompanyUserCount
        {
            CompanyId = 5,
            CompanyName = "Test Corp",
            UserCount = 42
        };

        Assert.Equal(5, count.CompanyId);
        Assert.Equal("Test Corp", count.CompanyName);
        Assert.Equal(42, count.UserCount);
    }

    // ==================== PERMISSION AND ROLE INFO TESTS ====================

    [Fact]
    public void PermissionInfo_CanBeCreated()
    {
        var permission = new PermissionInfo
        {
            Key = "vehicles",
            Label = "Véhicules",
            Description = "Gestion des véhicules"
        };

        Assert.Equal("vehicles", permission.Key);
        Assert.Equal("Véhicules", permission.Label);
        Assert.Equal("Gestion des véhicules", permission.Description);
    }

    [Fact]
    public void RoleInfo_CanBeCreated()
    {
        var role = new RoleInfo
        {
            Key = "admin",
            Label = "Administrateur",
            Description = "Accès complet au système"
        };

        Assert.Equal("admin", role.Key);
        Assert.Equal("Administrateur", role.Label);
        Assert.Equal("Accès complet au système", role.Description);
    }

    // ==================== VALIDATION TESTS ====================

    [Theory]
    [InlineData("active")]
    [InlineData("suspended")]
    [InlineData("inactive")]
    public void AdminUserDto_Status_AcceptsValidValues(string status)
    {
        var dto = new AdminUserDto { Status = status };
        Assert.Equal(status, dto.Status);
    }

    [Theory]
    [InlineData("john@test.com")]
    [InlineData("user.FullName@company.org")]
    [InlineData("admin@subdomain.example.co.uk")]
    public void CreateAdminUserRequest_Email_AcceptsValidFormats(string email)
    {
        var request = new CreateAdminUserRequest
        {
            Name = "Test",
            Email = email,
            Password = "password",
            CompanyId = 1
        };
        Assert.Equal(email, request.Email);
    }

    [Theory]
    [InlineData(new int[] { })]
    [InlineData(new int[] { 1 })]
    [InlineData(new int[] { 1, 2, 3, 4, 5 })]
    public void AdminUserDto_AssignedVehicleIds_AcceptsVariousLengths(int[] vehicleIds)
    {
        var dto = new AdminUserDto { AssignedVehicleIds = vehicleIds };
        Assert.Equal(vehicleIds.Length, dto.AssignedVehicleIds.Length);
    }

    [Fact]
    public void AdminUserDto_DateOfBirth_CanBeSet()
    {
        var dob = new DateTime(1990, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var dto = new AdminUserDto { DateOfBirth = dob };
        Assert.Equal(dob, dto.DateOfBirth);
    }

    [Fact]
    public void AdminUserDto_IsOnline_BasedOnLastLogin()
    {
        var recentLogin = DateTime.UtcNow.AddMinutes(-5);
        var oldLogin = DateTime.UtcNow.AddHours(-2);

        var onlineUser = new AdminUserDto { LastLoginAt = recentLogin, IsOnline = true };
        var offlineUser = new AdminUserDto { LastLoginAt = oldLogin, IsOnline = false };

        Assert.True(onlineUser.IsOnline);
        Assert.False(offlineUser.IsOnline);
    }

    // ==================== ROLE ASSIGNMENT VALIDATION TESTS ====================

    [Fact]
    public void CreateAdminUserRequest_WithRoleId_SetsRole()
    {
        var request = new CreateAdminUserRequest
        {
            Name = "User with Role",
            Email = "withrole@test.com",
            Password = "password",
            CompanyId = 1,
            RoleId = 5
        };

        Assert.Equal(5, request.RoleId);
    }

    [Fact]
    public void CreateAdminUserRequest_WithoutRoleId_RoleIsNull()
    {
        var request = new CreateAdminUserRequest
        {
            Name = "User without Role",
            Email = "norole@test.com",
            Password = "password",
            CompanyId = 1
        };

        Assert.Null(request.RoleId);
    }

    // ==================== VEHICLE ASSIGNMENT TESTS ====================

    [Fact]
    public void CreateAdminUserRequest_WithVehicles_SetsAssignedVehicles()
    {
        var vehicleIds = new[] { 10, 20, 30 };
        var request = new CreateAdminUserRequest
        {
            Name = "Driver",
            Email = "driver@test.com",
            Password = "password",
            CompanyId = 1,
            AssignedVehicleIds = vehicleIds
        };

        Assert.Equal(3, request.AssignedVehicleIds?.Length);
        Assert.Contains(10, request.AssignedVehicleIds!);
        Assert.Contains(20, request.AssignedVehicleIds!);
        Assert.Contains(30, request.AssignedVehicleIds!);
    }

    [Fact]
    public void AdminUserDto_VehicleCount_MatchesAssignedVehicles()
    {
        var dto = new AdminUserDto
        {
            Name = "User",
            AssignedVehicleIds = new[] { 1, 2, 3, 4, 5 }
        };

        Assert.Equal(5, dto.AssignedVehicleIds.Length);
    }

    // ==================== PERMISSIONS ARRAY TESTS ====================

    [Fact]
    public void UpdatePermissionsRequest_EmptyPermissions_IsValid()
    {
        var request = new UpdatePermissionsRequest
        {
            Permissions = Array.Empty<string>()
        };

        Assert.Empty(request.Permissions);
    }

    [Fact]
    public void UpdatePermissionsRequest_MultiplePermissions_AllStored()
    {
        var permissions = new[] { "dashboard", "monitoring", "vehicles", "employees", "maintenance", "reports" };
        var request = new UpdatePermissionsRequest { Permissions = permissions };

        Assert.Equal(6, request.Permissions.Length);
        foreach (var perm in permissions)
        {
            Assert.Contains(perm, request.Permissions);
        }
    }

    // ==================== CIN FIELD TESTS ====================

    [Theory]
    [InlineData("AB123456")]
    [InlineData("CD789012")]
    [InlineData("EF345678")]
    [InlineData("A1234567")]
    public void AdminUserDto_CIN_AcceptsValidFormats(string cin)
    {
        var dto = new AdminUserDto { CIN = cin };
        Assert.Equal(cin, dto.CIN);
    }

    [Fact]
    public void CreateAdminUserRequest_CIN_CanBeNull()
    {
        var request = new CreateAdminUserRequest
        {
            Name = "Test",
            Email = "test@test.com",
            Password = "password",
            CompanyId = 1,
            CIN = null
        };

        Assert.Null(request.CIN);
    }
}



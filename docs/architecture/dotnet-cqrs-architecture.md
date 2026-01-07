# Architecture .NET CQRS - Monolithe Modulaire

## 📋 Contexte

Ce document propose une architecture **CQRS (Command Query Responsibility Segregation)** pour le projet GisAPI, en adoptant une approche **Monolithe Modulaire** qui permet:
- Séparation claire des responsabilités
- Évolutivité vers des microservices si nécessaire
- Testabilité améliorée
- Maintenabilité à long terme

---

## 🏗️ Architecture Actuelle vs Proposée

### État Actuel (Simple CRUD)
```
GisAPI/
├── Controllers/          # Logique métier mélangée
├── Models/              # Entités + DTOs mélangés
├── DTOs/                # Quelques DTOs
├── Data/                # DbContext unique
└── Program.cs           # Configuration monolithique
```

**Problèmes identifiés:**
- Contrôleurs avec logique métier directe
- Pas de séparation Command/Query
- Validation dispersée
- Difficile à tester unitairement
- Couplage fort avec EF Core

### Architecture Proposée (CQRS Modulaire)
```
src/
├── GisAPI.Api/                    # Couche Présentation (Host)
├── GisAPI.Application/            # Couche Application (CQRS)
├── GisAPI.Domain/                 # Couche Domaine (Entités, Règles)
├── GisAPI.Infrastructure/         # Couche Infrastructure (Persistance)
└── GisAPI.Shared/                 # Contrats partagés
```

---

## 📁 Structure Détaillée des Projets

### 1. GisAPI.Domain (Class Library)
> **Responsabilité**: Entités, Value Objects, Règles métier, Interfaces

```
GisAPI.Domain/
├── Entities/
│   ├── Company.cs
│   ├── User.cs
│   ├── Vehicle.cs
│   ├── Employee.cs
│   ├── GpsDevice.cs
│   ├── GpsPosition.cs
│   ├── Geofence.cs
│   ├── MaintenanceRecord.cs
│   ├── VehicleCost.cs
│   └── Subscription.cs
├── ValueObjects/
│   ├── Email.cs
│   ├── PhoneNumber.cs
│   ├── Coordinate.cs
│   ├── Money.cs
│   └── VehiclePlate.cs
├── Enums/
│   ├── VehicleStatus.cs
│   ├── EmployeeRole.cs
│   ├── AlertSeverity.cs
│   ├── MaintenanceType.cs
│   └── CostType.cs
├── Events/
│   ├── VehicleCreatedEvent.cs
│   ├── AlertTriggeredEvent.cs
│   ├── GeofenceEnteredEvent.cs
│   └── MaintenanceScheduledEvent.cs
├── Exceptions/
│   ├── DomainException.cs
│   ├── VehicleNotFoundException.cs
│   └── InvalidOperationException.cs
├── Interfaces/
│   ├── Repositories/
│   │   ├── IVehicleRepository.cs
│   │   ├── IUserRepository.cs
│   │   ├── IEmployeeRepository.cs
│   │   ├── IGpsDeviceRepository.cs
│   │   ├── IGeofenceRepository.cs
│   │   ├── IMaintenanceRepository.cs
│   │   └── ICostRepository.cs
│   └── Services/
│       ├── IDateTimeProvider.cs
│       └── ICurrentUserService.cs
└── Common/
    ├── Entity.cs                  # Base class avec Id
    ├── AuditableEntity.cs         # CreatedAt, UpdatedAt
    └── IAggregateRoot.cs          # Marqueur pour DDD
```

### 2. GisAPI.Application (Class Library)
> **Responsabilité**: Use Cases, Commands, Queries, Handlers, Validation

```
GisAPI.Application/
├── Common/
│   ├── Interfaces/
│   │   ├── ICommand.cs
│   │   ├── IQuery.cs
│   │   ├── ICommandHandler.cs
│   │   ├── IQueryHandler.cs
│   │   └── IUnitOfWork.cs
│   ├── Behaviours/
│   │   ├── ValidationBehaviour.cs
│   │   ├── LoggingBehaviour.cs
│   │   ├── PerformanceBehaviour.cs
│   │   └── TransactionBehaviour.cs
│   ├── Exceptions/
│   │   ├── ValidationException.cs
│   │   ├── NotFoundException.cs
│   │   └── ForbiddenException.cs
│   └── Models/
│       ├── Result.cs
│       └── PaginatedList.cs
│
├── Features/
│   ├── Auth/
│   │   ├── Commands/
│   │   │   ├── Login/
│   │   │   │   ├── LoginCommand.cs
│   │   │   │   ├── LoginCommandHandler.cs
│   │   │   │   └── LoginCommandValidator.cs
│   │   │   ├── Register/
│   │   │   │   ├── RegisterCommand.cs
│   │   │   │   ├── RegisterCommandHandler.cs
│   │   │   │   └── RegisterCommandValidator.cs
│   │   │   └── RefreshToken/
│   │   │       └── ...
│   │   └── Queries/
│   │       └── GetCurrentUser/
│   │           ├── GetCurrentUserQuery.cs
│   │           └── GetCurrentUserQueryHandler.cs
│   │
│   ├── Vehicles/
│   │   ├── Commands/
│   │   │   ├── CreateVehicle/
│   │   │   │   ├── CreateVehicleCommand.cs
│   │   │   │   ├── CreateVehicleCommandHandler.cs
│   │   │   │   └── CreateVehicleCommandValidator.cs
│   │   │   ├── UpdateVehicle/
│   │   │   │   └── ...
│   │   │   ├── DeleteVehicle/
│   │   │   │   └── ...
│   │   │   └── AssignDriver/
│   │   │       └── ...
│   │   ├── Queries/
│   │   │   ├── GetVehicles/
│   │   │   │   ├── GetVehiclesQuery.cs
│   │   │   │   ├── GetVehiclesQueryHandler.cs
│   │   │   │   └── VehicleDto.cs
│   │   │   ├── GetVehicleById/
│   │   │   │   └── ...
│   │   │   └── GetVehicleLocations/
│   │   │       └── ...
│   │   └── EventHandlers/
│   │       └── VehicleCreatedEventHandler.cs
│   │
│   ├── Employees/
│   │   ├── Commands/
│   │   │   ├── CreateEmployee/
│   │   │   ├── UpdateEmployee/
│   │   │   └── DeleteEmployee/
│   │   └── Queries/
│   │       ├── GetEmployees/
│   │       ├── GetDrivers/
│   │       └── GetSupervisors/
│   │
│   ├── GpsDevices/
│   │   ├── Commands/
│   │   │   ├── RegisterDevice/
│   │   │   ├── AssignToVehicle/
│   │   │   └── UnassignDevice/
│   │   └── Queries/
│   │       ├── GetDevices/
│   │       ├── GetUnassignedDevices/
│   │       └── GetDevicePositions/
│   │
│   ├── Geofences/
│   │   ├── Commands/
│   │   │   ├── CreateGeofence/
│   │   │   ├── UpdateGeofence/
│   │   │   ├── DeleteGeofence/
│   │   │   └── AssignVehicles/
│   │   └── Queries/
│   │       ├── GetGeofences/
│   │       └── GetGeofenceEvents/
│   │
│   ├── Maintenance/
│   │   ├── Commands/
│   │   │   ├── ScheduleMaintenance/
│   │   │   ├── CompleteMaintenance/
│   │   │   └── CancelMaintenance/
│   │   └── Queries/
│   │       ├── GetMaintenanceRecords/
│   │       └── GetUpcomingMaintenance/
│   │
│   ├── Costs/
│   │   ├── Commands/
│   │   │   ├── RecordCost/
│   │   │   └── DeleteCost/
│   │   └── Queries/
│   │       ├── GetCosts/
│   │       └── GetCostSummary/
│   │
│   ├── Alerts/
│   │   ├── Commands/
│   │   │   ├── ResolveAlert/
│   │   │   └── ResolveAllAlerts/
│   │   └── Queries/
│   │       ├── GetAlerts/
│   │       └── GetUnreadAlertCount/
│   │
│   ├── Users/
│   │   ├── Commands/
│   │   │   ├── CreateUser/
│   │   │   ├── UpdateUser/
│   │   │   ├── UpdatePermissions/
│   │   │   └── DeleteUser/
│   │   └── Queries/
│   │       ├── GetUsers/
│   │       └── GetUserById/
│   │
│   └── Admin/
│       ├── Commands/
│       │   ├── CreateClient/
│       │   ├── SuspendClient/
│       │   ├── ActivateClient/
│       │   └── SetMaintenanceMode/
│       └── Queries/
│           ├── GetDashboardStats/
│           ├── GetClients/
│           ├── GetActivityLogs/
│           └── GetServiceHealth/
│
└── DependencyInjection.cs         # Extension pour services
```

### 3. GisAPI.Infrastructure (Class Library)
> **Responsabilité**: Persistance, Services externes, Implémentations

```
GisAPI.Infrastructure/
├── Persistence/
│   ├── GisDbContext.cs
│   ├── Configurations/
│   │   ├── VehicleConfiguration.cs
│   │   ├── UserConfiguration.cs
│   │   ├── EmployeeConfiguration.cs
│   │   ├── GpsDeviceConfiguration.cs
│   │   ├── GeofenceConfiguration.cs
│   │   └── ... (IEntityTypeConfiguration<T>)
│   ├── Repositories/
│   │   ├── VehicleRepository.cs
│   │   ├── UserRepository.cs
│   │   ├── EmployeeRepository.cs
│   │   ├── GpsDeviceRepository.cs
│   │   ├── GeofenceRepository.cs
│   │   ├── MaintenanceRepository.cs
│   │   └── CostRepository.cs
│   ├── Migrations/
│   │   └── ...
│   └── UnitOfWork.cs
├── Services/
│   ├── DateTimeProvider.cs
│   ├── CurrentUserService.cs
│   ├── JwtTokenService.cs
│   ├── PasswordHasher.cs
│   └── EmailService.cs
├── Messaging/
│   ├── RabbitMqPublisher.cs
│   └── RabbitMqConsumer.cs
├── Caching/
│   ├── ICacheService.cs
│   └── RedisCacheService.cs
└── DependencyInjection.cs
```

### 4. GisAPI.Api (Web API - Host)
> **Responsabilité**: Controllers, Middleware, Configuration

```
GisAPI.Api/
├── Controllers/
│   ├── AuthController.cs
│   ├── VehiclesController.cs
│   ├── EmployeesController.cs
│   ├── GpsDevicesController.cs
│   ├── GeofencesController.cs
│   ├── MaintenanceController.cs
│   ├── CostsController.cs
│   ├── AlertsController.cs
│   ├── UsersController.cs
│   └── Admin/
│       ├── AdminAuthController.cs
│       ├── AdminDashboardController.cs
│       ├── AdminClientsController.cs
│       └── ...
├── Middleware/
│   ├── ExceptionHandlingMiddleware.cs
│   ├── RequestLoggingMiddleware.cs
│   └── TenantMiddleware.cs
├── Filters/
│   ├── ApiExceptionFilterAttribute.cs
│   └── ValidationFilterAttribute.cs
├── Hubs/
│   └── TrackingHub.cs             # SignalR pour temps réel
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Docker.json
└── Program.cs
```

### 5. GisAPI.Shared (Class Library)
> **Responsabilité**: Contrats partagés, DTOs communs

```
GisAPI.Shared/
├── DTOs/
│   ├── Auth/
│   │   ├── LoginRequest.cs
│   │   ├── LoginResponse.cs
│   │   └── RegisterRequest.cs
│   ├── Vehicles/
│   │   ├── VehicleDto.cs
│   │   ├── VehicleLocationDto.cs
│   │   └── CreateVehicleRequest.cs
│   └── ... (tous les DTOs publics)
├── Constants/
│   ├── Roles.cs
│   ├── Permissions.cs
│   └── ErrorCodes.cs
└── Extensions/
    └── StringExtensions.cs
```

---

## 🔄 Pattern CQRS - Détail

### Structure d'une Command

```csharp
// Command (Input)
public record CreateVehicleCommand(
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    int Mileage
) : ICommand<int>;  // Retourne l'ID créé

// Validator (FluentValidation)
public class CreateVehicleCommandValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);
        
        RuleFor(x => x.Plate)
            .Matches(@"^\d+\s?[A-Z]+\s?\d+$")
            .When(x => !string.IsNullOrEmpty(x.Plate));
    }
}

// Handler (Logique métier)
public class CreateVehicleCommandHandler : ICommandHandler<CreateVehicleCommand, int>
{
    private readonly IVehicleRepository _vehicleRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public CreateVehicleCommandHandler(
        IVehicleRepository vehicleRepository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork)
    {
        _vehicleRepository = vehicleRepository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> Handle(CreateVehicleCommand command, CancellationToken ct)
    {
        var vehicle = new Vehicle
        {
            Name = command.Name,
            Type = command.Type,
            Brand = command.Brand,
            Model = command.Model,
            Plate = command.Plate,
            Year = command.Year,
            Color = command.Color,
            Mileage = command.Mileage,
            CompanyId = _currentUser.CompanyId,
            Status = VehicleStatus.Available
        };

        await _vehicleRepository.AddAsync(vehicle, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return vehicle.Id;
    }
}
```

### Structure d'une Query

```csharp
// Query (Input avec filtres)
public record GetVehiclesQuery(
    string? SearchTerm,
    VehicleStatus? Status,
    int Page = 1,
    int PageSize = 20
) : IQuery<PaginatedList<VehicleDto>>;

// Handler (Lecture optimisée)
public class GetVehiclesQueryHandler : IQueryHandler<GetVehiclesQuery, PaginatedList<VehicleDto>>
{
    private readonly GisDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetVehiclesQueryHandler(GisDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<VehicleDto>> Handle(GetVehiclesQuery query, CancellationToken ct)
    {
        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == _currentUser.CompanyId);

        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            vehiclesQuery = vehiclesQuery.Where(v => 
                v.Name.Contains(query.SearchTerm) || 
                v.Plate.Contains(query.SearchTerm));
        }

        if (query.Status.HasValue)
        {
            vehiclesQuery = vehiclesQuery.Where(v => v.Status == query.Status.Value);
        }

        return await vehiclesQuery
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(/* projection */))
            .ToPaginatedListAsync(query.Page, query.PageSize, ct);
    }
}
```

### Controller Simplifié

```csharp
[ApiController]
[Route("api/vehicles")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly ISender _sender;  // MediatR

    public VehiclesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<VehicleDto>>> GetVehicles(
        [FromQuery] GetVehiclesQuery query)
    {
        return Ok(await _sender.Send(query));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleDto>> GetVehicle(int id)
    {
        return Ok(await _sender.Send(new GetVehicleByIdQuery(id)));
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateVehicle(CreateVehicleCommand command)
    {
        var id = await _sender.Send(command);
        return CreatedAtAction(nameof(GetVehicle), new { id }, id);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateVehicle(int id, UpdateVehicleCommand command)
    {
        if (id != command.Id) return BadRequest();
        await _sender.Send(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteVehicle(int id)
    {
        await _sender.Send(new DeleteVehicleCommand(id));
        return NoContent();
    }
}
```

---

## 📦 Packages NuGet Recommandés

### GisAPI.Domain
```xml
<ItemGroup>
  <!-- Aucune dépendance externe - Domain pur -->
</ItemGroup>
```

### GisAPI.Application
```xml
<ItemGroup>
  <PackageReference Include="MediatR" Version="12.2.0" />
  <PackageReference Include="FluentValidation" Version="11.9.0" />
  <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.9.0" />
  <PackageReference Include="AutoMapper" Version="13.0.1" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
</ItemGroup>
```

### GisAPI.Infrastructure
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.3" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
  <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
  <PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
  <PackageReference Include="StackExchange.Redis" Version="2.7.33" />
</ItemGroup>
```

### GisAPI.Api
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.0" />
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.0" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
  <PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
  <PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />
</ItemGroup>
```

---

## 🔧 Configuration Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Layers DI
builder.Services
    .AddApplication()           // MediatR, Validators, Behaviours
    .AddInfrastructure(builder.Configuration)  // EF, Repositories
    .AddApiServices();          // Controllers, Auth, Swagger

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TrackingHub>("/hubs/tracking");
app.MapHealthChecks("/health");

app.Run();
```

---

## 🚀 Plan de Migration

### Phase 1: Préparation (1-2 jours)
1. Créer la structure de solution avec les 5 projets
2. Configurer les références entre projets
3. Ajouter les packages NuGet

### Phase 2: Domain Layer (1 jour)
1. Déplacer les entités existantes
2. Créer les Value Objects
3. Définir les interfaces des repositories

### Phase 3: Infrastructure Layer (2 jours)
1. Migrer le DbContext avec configurations séparées
2. Implémenter les repositories
3. Configurer le UnitOfWork

### Phase 4: Application Layer (3-5 jours)
1. Créer les Commands/Queries par Feature
2. Implémenter les Handlers
3. Ajouter les Validators
4. Configurer MediatR Behaviours

### Phase 5: API Layer (1-2 jours)
1. Refactorer les Controllers pour utiliser MediatR
2. Ajouter les Middlewares
3. Configurer Swagger/OpenAPI

### Phase 6: Tests (2-3 jours)
1. Tests unitaires pour les Handlers
2. Tests d'intégration pour les Repositories
3. Tests E2E pour les Controllers

---

## 📊 Avantages de cette Architecture

| Aspect | Bénéfice |
|--------|----------|
| **Séparation des responsabilités** | Chaque couche a un rôle unique |
| **Testabilité** | Handlers testables en isolation |
| **Évolutivité** | Facile d'ajouter de nouvelles features |
| **Migration microservices** | Chaque Feature peut devenir un service |
| **Performance** | Queries optimisées en lecture seule |
| **Maintenabilité** | Code organisé et prévisible |
| **Validation** | Centralisée avec FluentValidation |
| **Logging** | Automatique via MediatR Behaviours |

---

## 📁 Structure Finale de la Solution

```
GisAPI.sln
├── src/
│   ├── GisAPI.Domain/
│   │   └── GisAPI.Domain.csproj
│   ├── GisAPI.Application/
│   │   └── GisAPI.Application.csproj
│   ├── GisAPI.Infrastructure/
│   │   └── GisAPI.Infrastructure.csproj
│   ├── GisAPI.Api/
│   │   └── GisAPI.Api.csproj
│   └── GisAPI.Shared/
│       └── GisAPI.Shared.csproj
├── tests/
│   ├── GisAPI.Domain.Tests/
│   ├── GisAPI.Application.Tests/
│   ├── GisAPI.Infrastructure.Tests/
│   └── GisAPI.Api.Tests/
└── docker/
    └── Dockerfile
```

---

*Architecture proposée le 24/12/2024*
*Basée sur Clean Architecture + CQRS + MediatR*

using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.MaintenanceTemplates.Commands;

public class CreateMaintenanceTemplateCommandHandler : IRequestHandler<CreateMaintenanceTemplateCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPublisher _publisher;

    public CreateMaintenanceTemplateCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<int> Handle(CreateMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        if (!request.IntervalKm.HasValue && !request.IntervalMonths.HasValue)
            throw new ArgumentException("At least one interval (km or months) must be specified");

        var template = new MaintenanceTemplate
        {
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            Priority = request.Priority,
            IntervalKm = request.IntervalKm,
            IntervalMonths = request.IntervalMonths,
            EstimatedCost = request.EstimatedCost,
            IsActive = request.IsActive,
            CompanyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set")
        };

        _context.MaintenanceTemplates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);

        // Notify company admins
        var companyId = _tenantService.CompanyId ?? 0;
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
        if (actor != null && companyId > 0)
        {
            _ = _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "maintenance_created", template.Name, template.Id, "maintenance"
            ), cancellationToken);
        }

        return template.Id;
    }
}

public class UpdateMaintenanceTemplateCommandHandler : IRequestHandler<UpdateMaintenanceTemplateCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateMaintenanceTemplateCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _context.MaintenanceTemplates
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (template == null) return false;

        if (request.Name != null) template.Name = request.Name;
        if (request.Description != null) template.Description = request.Description;
        if (request.Category != null) template.Category = request.Category;
        if (request.Priority != null) template.Priority = request.Priority;
        if (request.IntervalKm.HasValue) template.IntervalKm = request.IntervalKm;
        if (request.IntervalMonths.HasValue) template.IntervalMonths = request.IntervalMonths;
        if (request.EstimatedCost.HasValue) template.EstimatedCost = request.EstimatedCost;
        if (request.IsActive.HasValue) template.IsActive = request.IsActive.Value;

        template.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteMaintenanceTemplateCommandHandler : IRequestHandler<DeleteMaintenanceTemplateCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteMaintenanceTemplateCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(DeleteMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _context.MaintenanceTemplates
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (template == null) return false;

        _context.MaintenanceTemplates.Remove(template);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}




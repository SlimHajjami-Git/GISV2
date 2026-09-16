using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
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
        // Refus métier en DomainException (400 { message }) : l'ArgumentException tombait
        // dans le repli 500 « An unexpected error occurred » du middleware et le formulaire
        // ne pouvait rien expliquer (recette GPA, DEF-031).
        if (!request.IntervalKm.HasValue && !request.IntervalMonths.HasValue)
            throw new DomainException(
                "Indiquez au moins une périodicité : un intervalle en kilomètres ou en mois.");

        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Aucune société n'est associée à votre compte : modèle non créé.");

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
            WarningKm = request.WarningKm ?? 1000,
            WarningDays = request.WarningDays ?? 30,
            CriticalKm = request.CriticalKm ?? 0,
            CriticalDays = request.CriticalDays ?? 0,
            CompanyId = companyId
        };

        _context.MaintenanceTemplates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);

        // Notify company admins
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
        if (actor != null && companyId > 0)
        {
            await _publisher.Publish(new AdminActionNotificationEvent(
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
        if (request.WarningKm.HasValue) template.WarningKm = request.WarningKm.Value;
        if (request.WarningDays.HasValue) template.WarningDays = request.WarningDays.Value;
        if (request.CriticalKm.HasValue) template.CriticalKm = request.CriticalKm.Value;
        if (request.CriticalDays.HasValue) template.CriticalDays = request.CriticalDays.Value;

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

        // Les clés étrangères vers maintenance_templates sont en ON DELETE CASCADE :
        // supprimer un modèle effaçait les journaux des entretiens déjà réalisés,
        // alors que leurs dépenses (sans lien vers le modèle) restaient. L'historique
        // du véhicule se vidait pendant que Dépenses et « Coûts maintenance »
        // montraient toujours l'intervention (recette GPA, DEF-016).
        // Un modèle qui a servi se désactive donc au lieu d'être supprimé : aucune
        // donnée n'est perdue. Sans historique, la suppression reste possible (elle
        // ne retire que des échéances à venir).
        var doneCount = await _context.MaintenanceLogs
            .CountAsync(l => l.TemplateId == template.Id, cancellationToken);

        if (doneCount > 0)
            throw new ConflictException(HistoryConflictMessage(template, doneCount));

        _context.MaintenanceTemplates.Remove(template);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string HistoryConflictMessage(MaintenanceTemplate template, int doneCount)
    {
        var history = doneCount > 1
            ? $"{doneCount} entretiens réalisés y sont rattachés"
            : "1 entretien réalisé y est rattaché";
        var advice = template.IsActive
            ? "Désactivez-le plutôt : il ne sera plus proposé ni surveillé, et son historique restera consultable."
            : "Il est déjà désactivé : il n'est plus proposé ni surveillé, et son historique reste consultable.";
        return $"Le modèle « {template.Name} » ne peut pas être supprimé : {history}, " +
               $"et la suppression effacerait cet historique. {advice}";
    }
}




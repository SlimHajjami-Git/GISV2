using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.MaintenanceTemplates.Queries;

public class GetMaintenanceTemplatesQueryHandler : IRequestHandler<GetMaintenanceTemplatesQuery, PaginatedList<MaintenanceTemplateDto>>
{
    private readonly IGisDbContext _context;

    public GetMaintenanceTemplatesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<MaintenanceTemplateDto>> Handle(GetMaintenanceTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.MaintenanceTemplates.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(t => t.Category == request.Category);

        if (request.IsActive.HasValue)
            query = query.Where(t => t.IsActive == request.IsActive.Value);

        query = query.OrderBy(t => t.Category).ThenBy(t => t.Name);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new MaintenanceTemplateDto(
                t.Id, t.Name, t.Description, t.Category, t.Priority,
                t.IntervalKm, t.IntervalMonths, t.EstimatedCost,
                t.IsActive, t.CreatedAt, t.UpdatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PaginatedList<MaintenanceTemplateDto>(items, totalCount, request.Page, request.PageSize);
    }
}




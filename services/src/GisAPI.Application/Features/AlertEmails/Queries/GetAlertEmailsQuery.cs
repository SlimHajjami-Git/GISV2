using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AlertEmails.Queries;

public record GetAlertEmailsQuery(string? AlertType = null) : IRequest<List<AlertEmailDto>>;

public class GetAlertEmailsQueryHandler : IRequestHandler<GetAlertEmailsQuery, List<AlertEmailDto>>
{
    private readonly IGisDbContext _context;

    public GetAlertEmailsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<AlertEmailDto>> Handle(GetAlertEmailsQuery request, CancellationToken ct)
    {
        var query = _context.AlertEmails.AsQueryable();

        if (!string.IsNullOrEmpty(request.AlertType))
            query = query.Where(a => a.AlertType == request.AlertType);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AlertEmailDto(a.Id, a.Email, a.AlertType, a.CreatedAt))
            .ToListAsync(ct);
    }
}

using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AlertEmails.Commands;

public record UpdateAlertEmailCommand(int Id, string Email, string AlertType) : IRequest;

public class UpdateAlertEmailCommandHandler : IRequestHandler<UpdateAlertEmailCommand>
{
    private readonly IGisDbContext _context;

    public UpdateAlertEmailCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateAlertEmailCommand request, CancellationToken ct)
    {
        var entity = await _context.AlertEmails
            .FirstOrDefaultAsync(a => a.Id == request.Id, ct);

        if (entity == null)
            throw new NotFoundException("AlertEmail", request.Id);

        entity.Email = request.Email;
        entity.AlertType = request.AlertType;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}

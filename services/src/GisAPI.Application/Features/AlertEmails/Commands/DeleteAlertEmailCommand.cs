using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AlertEmails.Commands;

public record DeleteAlertEmailCommand(int Id) : IRequest;

public class DeleteAlertEmailCommandHandler : IRequestHandler<DeleteAlertEmailCommand>
{
    private readonly IGisDbContext _context;

    public DeleteAlertEmailCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteAlertEmailCommand request, CancellationToken ct)
    {
        var entity = await _context.AlertEmails
            .FirstOrDefaultAsync(a => a.Id == request.Id, ct);

        if (entity == null)
            throw new NotFoundException("AlertEmail", request.Id);

        _context.AlertEmails.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }
}

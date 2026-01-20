using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Commands.DeleteSociete;

public class DeleteSocieteCommandHandler : IRequestHandler<DeleteSocieteCommand>
{
    private readonly IGisDbContext _context;

    public DeleteSocieteCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteSocieteCommand request, CancellationToken ct)
    {
        var societe = await _context.Societes
            .Include(s => s.Users)
            .Include(s => s.Vehicles)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException("Societe", request.Id);

        if (societe.Users.Any())
            throw new DomainException($"Impossible de supprimer cette société car elle contient {societe.Users.Count} utilisateur(s)");

        if (societe.Vehicles.Any())
            throw new DomainException($"Impossible de supprimer cette société car elle contient {societe.Vehicles.Count} véhicule(s)");

        _context.Societes.Remove(societe);
        await _context.SaveChangesAsync(ct);
    }
}

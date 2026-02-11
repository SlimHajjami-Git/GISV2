using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Employees.Commands.DeleteEmployee;

public record DeleteEmployeeCommand(int Id) : IRequest;

public class DeleteEmployeeCommandHandler : IRequestHandler<DeleteEmployeeCommand>
{
    private readonly IGisDbContext _context;

    public DeleteEmployeeCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteEmployeeCommand request, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(new object[] { request.Id }, ct);
        if (user == null)
            throw new DomainException("Employé introuvable");

        // Unassign from any vehicle
        var vehicles = await _context.Vehicles
            .Where(v => v.AssignedDriverId == user.Id)
            .ToListAsync(ct);
        foreach (var v in vehicles)
            v.AssignedDriverId = null;

        // Remove user-vehicle assignments
        var userVehicles = await _context.UserVehicles
            .Where(uv => uv.UserId == user.Id)
            .ToListAsync(ct);
        foreach (var uv in userVehicles)
            _context.UserVehicles.Remove(uv);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
    }
}

using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Suppliers.Commands;

public record UpdateSupplierServicesCommand(
    int SupplierId,
    List<string> Services
) : ICommand<bool>;

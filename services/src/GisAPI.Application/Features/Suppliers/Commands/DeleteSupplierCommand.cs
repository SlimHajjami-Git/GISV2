using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Suppliers.Commands;

public record DeleteSupplierCommand(int Id) : ICommand<bool>;

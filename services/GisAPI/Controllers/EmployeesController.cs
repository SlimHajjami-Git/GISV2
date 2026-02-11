using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Employees.Queries.GetEmployees;
using GisAPI.Application.Features.Employees.Commands.CreateEmployee;
using GisAPI.Application.Features.Employees.Commands.UpdateEmployee;
using GisAPI.Application.Features.Employees.Commands.DeleteEmployee;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<EmployeeDto>>> GetEmployees([FromQuery] string? role = null, [FromQuery] string? status = null)
    {
        var employees = await _mediator.Send(new GetEmployeesQuery(role, status));
        return Ok(employees);
    }

    [HttpGet("drivers")]
    public async Task<ActionResult<List<EmployeeDto>>> GetDrivers()
    {
        var drivers = await _mediator.Send(new GetEmployeesQuery("driver"));
        return Ok(drivers);
    }

    [HttpGet("supervisors")]
    public async Task<ActionResult<List<EmployeeDto>>> GetSupervisors()
    {
        var supervisors = await _mediator.Send(new GetEmployeesQuery("supervisor"));
        return Ok(supervisors);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<EmployeeDto>> GetEmployee(int id)
    {
        var employees = await _mediator.Send(new GetEmployeesQuery());
        var employee = employees.FirstOrDefault(e => e.Id == id);
        if (employee == null)
            return NotFound();
        return Ok(employee);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> CreateEmployee([FromBody] CreateEmployeeRequest request)
    {
        var command = new CreateEmployeeCommand(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone,
            request.EmployeeRole ?? "driver",
            request.PermitNumber,
            request.PermitType,
            request.PermitExpiry,
            request.CIN,
            request.DateOfBirth,
            request.HireDate,
            request.AssignVehicleId
        );

        var employee = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetEmployee), new { id = employee.Id }, employee);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEmployee(int id, [FromBody] UpdateEmployeeRequest request)
    {
        var command = new UpdateEmployeeCommand(
            id,
            request.FirstName,
            request.LastName,
            request.Email,
            request.Phone,
            request.EmployeeRole ?? "driver",
            request.Status,
            request.PermitNumber,
            request.PermitType,
            request.PermitExpiry,
            request.CIN,
            request.DateOfBirth,
            request.HireDate,
            request.AssignVehicleId
        );

        await _mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEmployee(int id)
    {
        await _mediator.Send(new DeleteEmployeeCommand(id));
        return NoContent();
    }
}

// Request DTOs
public record CreateEmployeeRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? EmployeeRole,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignVehicleId
);

public record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? EmployeeRole,
    string? Status,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignVehicleId
);

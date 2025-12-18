using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Data;
using GisAPI.Models;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly GisDbContext _context;

    public EmployeesController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<Employee>>> GetEmployees()
    {
        var companyId = GetCompanyId();

        var employees = await _context.Employees
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.Name)
            .ToListAsync();

        return Ok(employees);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Employee>> GetEmployee(int id)
    {
        var companyId = GetCompanyId();

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

        if (employee == null)
            return NotFound();

        return Ok(employee);
    }

    [HttpGet("drivers")]
    public async Task<ActionResult<List<Employee>>> GetDrivers()
    {
        var companyId = GetCompanyId();

        var drivers = await _context.Employees
            .Where(e => e.CompanyId == companyId && e.Role == "driver" && e.Status == "active")
            .OrderBy(e => e.Name)
            .ToListAsync();

        return Ok(drivers);
    }

    [HttpGet("supervisors")]
    public async Task<ActionResult<List<Employee>>> GetSupervisors()
    {
        var companyId = GetCompanyId();

        var supervisors = await _context.Employees
            .Where(e => e.CompanyId == companyId && e.Role == "supervisor" && e.Status == "active")
            .OrderBy(e => e.Name)
            .ToListAsync();

        return Ok(supervisors);
    }

    [HttpPost]
    public async Task<ActionResult<Employee>> CreateEmployee([FromBody] Employee employee)
    {
        var companyId = GetCompanyId();
        employee.CompanyId = companyId;
        employee.CreatedAt = DateTime.UtcNow;

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetEmployee), new { id = employee.Id }, employee);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateEmployee(int id, [FromBody] Employee updated)
    {
        var companyId = GetCompanyId();

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

        if (employee == null)
            return NotFound();

        employee.Name = updated.Name;
        employee.Email = updated.Email;
        employee.Phone = updated.Phone;
        employee.Role = updated.Role;
        employee.Status = updated.Status;
        employee.HireDate = updated.HireDate;
        employee.LicenseNumber = updated.LicenseNumber;
        employee.LicenseExpiry = updated.LicenseExpiry;
        employee.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteEmployee(int id)
    {
        var companyId = GetCompanyId();

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

        if (employee == null)
            return NotFound();

        _context.Employees.Remove(employee);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

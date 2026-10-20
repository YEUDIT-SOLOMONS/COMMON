using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// השלמה אוטומטית של כתובות לפי מאגר היישובים והרחובות הרשמי של ישראל (ראו AddressDirectory)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly IAddressDirectory _directory;

    public AddressesController(IAddressDirectory directory)
    {
        _directory = directory;
    }

    [HttpGet("cities")]
    public async Task<IActionResult> Cities([FromQuery] string? q)
    {
        return Ok(await _directory.SearchCitiesAsync(q ?? string.Empty));
    }

    [HttpGet("streets")]
    public async Task<IActionResult> Streets([FromQuery] string? city, [FromQuery] string? q)
    {
        return Ok(await _directory.SearchStreetsAsync(city ?? string.Empty, q ?? string.Empty));
    }
}

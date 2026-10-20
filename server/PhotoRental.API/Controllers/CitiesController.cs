using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// מיקומי ערים למיון "הכי קרוב אליי"
[ApiController]
[Route("api/cities")]
[AllowAnonymous]
public class CitiesController : ControllerBase
{
    private readonly ICityCoordsService _coords;
    private readonly IItemDistanceService _distances;

    public CitiesController(ICityCoordsService coords, IItemDistanceService distances)
    {
        _coords = coords;
        _distances = distances;
    }

    [HttpGet("coords")]
    public async Task<IActionResult> Coords([FromQuery] string? names)
    {
        var list = (names ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        return Ok(await _coords.GetAsync(list));
    }

    [HttpGet("distances")]
    public async Task<IActionResult> Distances([FromQuery] double? lat, [FromQuery] double? lng, [FromQuery] string? city) =>
        Ok(await _distances.GetAsync(lat, lng, city, User.FindFirstValue(ClaimTypes.NameIdentifier)));
}

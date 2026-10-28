using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace EntraApi.Controllers;

[ApiController]
[Authorize]
[RequiredScope("access_as_user")]
[Route("api/[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries =
        ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

    [HttpGet]
    public IEnumerable<object> Get() =>
        Enumerable.Range(1, 5).Select(i => new
        {
            date = DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
            temperatureC = Random.Shared.Next(-20, 55),
            summary = Summaries[Random.Shared.Next(Summaries.Length)]
        });
}

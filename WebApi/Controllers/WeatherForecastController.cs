using MediatorLib;
using Microsoft.AspNetCore.Mvc;
using ServiceLib;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private readonly IMediator _mediator;
        public WeatherForecastController(IMediator mediator)
        {
            _mediator = mediator;
        }


        [HttpGet("{city}")]
        public async Task<IActionResult> Get(string city)
        {
            var result = await _mediator.SendAsync(new GetWeatherQuery(city));

            return Ok(result);
        }
    }
}

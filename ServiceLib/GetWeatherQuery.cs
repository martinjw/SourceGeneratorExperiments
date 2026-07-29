using MediatorLib.Mediator;

namespace ServiceLib
{
    public record GetWeatherQuery(string City) : IRequest<WeatherResult>;
}

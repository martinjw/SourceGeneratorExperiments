using MediatorLib;

namespace ServiceLib
{
    public record GetWeatherQuery(string City) : IRequest<WeatherResult>;
}

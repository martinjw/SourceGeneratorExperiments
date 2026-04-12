using MediatorLib;

namespace ServiceLib;

[RequestHandler]
public class GetWeatherHandler : IRequestHandler<GetWeatherQuery, WeatherResult>
{
    public Task<WeatherResult> HandleAsync(GetWeatherQuery request, CancellationToken cancellationToken)
    {
        // Fake data for demo
        var result = new WeatherResult(request.City, 21);
        return Task.FromResult(result);
    }
}
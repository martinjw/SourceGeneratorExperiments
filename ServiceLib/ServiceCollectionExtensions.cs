using MediatorLib;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceLib
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddServiceLib(this IServiceCollection services)
        {
            services.AddSingleton<IMediator, Mediator>();
            services.AddTransient<IRequestHandler<GetWeatherQuery, WeatherResult>, GetWeatherHandler>();

            return services;
        }
    }
}
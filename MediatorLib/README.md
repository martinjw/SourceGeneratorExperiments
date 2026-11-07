# MediatorMapper

MediatorMapper is a small mediator and object-mapping library for .NET. It provides
request/notification contracts, a mediator backed by dependency injection, and
mapping APIs designed to work with the companion `MediatorMapperGenerator` source
generator.

## Mediator

Define a request and its handler with `IRequest<TResponse>` and
`IRequestHandler<TRequest, TResponse>`. For requests without a response, use
`IRequest` and `IRequestHandler<TRequest>`. Notifications implement
`INotification` and can have multiple `INotificationHandler<TNotification>`
handlers.

Send requests through `IMediator.Send` and publish notifications through
`IMediator.PublishAsync`. Handlers are resolved from dependency injection, so
their own dependencies can be injected by the container.

### Register handlers by scanning assemblies

In an ASP.NET Core `Program.cs`, register the mediator and scan the assembly
containing your handlers:

```csharp
using MediatorMapper.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(typeof(GetWeatherHandler).Assembly);

var app = builder.Build();
app.Run();
```

`AddMediator` registers the mediator and discovered handlers with
`IServiceCollection`. You can also build the registry explicitly:

```csharp
var registry = HandlerRegistryBuilder.Build(
    new[] { typeof(GetWeatherHandler).Assembly });

builder.Services.AddMediator(registry);
```

### Register handlers manually

Use `HandlerRegistry` when you want to choose the handler registrations
yourself. Request handler keys are the request types; notification handler keys
are notification types and can have multiple handler types.

```csharp
var registry = new HandlerRegistry();
registry.RequestHandlers.Add(
    typeof(GetWeatherQuery),
    typeof(GetWeatherHandler));

registry.NotificationHandlers.Add(
    typeof(WeatherUpdated),
    new List<Type> { typeof(WeatherUpdatedHandler) });

builder.Services.AddMediator(registry);
```

`AddMediator(registry)` registers the registry, `IMediator`, and the handler
types with dependency injection.

## Mapping

`MappingProfile` is the base class for declaring mappings with
`CreateMap<TSource, TDestination>()`. The profile and fluent expressions
(`ForMember`, `ReverseMap`, and `AfterMap`) describe mappings for
`MediatorMappingGenerator`; they are not executed as runtime configuration.

Add the `MediatorMappingGenerator` NuGet package as an analyzer to the project
containing your mapping profiles. The generator produces mapping code and
registers it with `MapperConfiguration`. Then use `IMapper` (or `Mapper`) to
map objects:

```csharp
using MediatorMapper.Mapping;

public sealed class PersonProfile : MappingProfile
{
    public PersonProfile()
    {
        CreateMap<Person, PersonDto>().ReverseMap();
    }
}

// In application code:
IMapper mapper = new Mapper();
PersonDto dto = mapper.Map<PersonDto>(person);
```

Mappings are stored in the shared `MapperConfiguration.Instance`. Calling
`Map<TDestination>` requires a generated mapping for the source and destination
types, or a source and destination type that are the same. Enumerable mappings
are supported when a mapping exists for their element types.

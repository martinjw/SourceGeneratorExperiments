using MediatorLib;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

//automatically scan and register all handlers in the assembly
//reflection based registration
//builder.Services.AddMediatorLib(typeof(GetWeatherHandler).Assembly);
//or use the source generator to produce a static registry and register it
builder.Services.AddMediatorLib(HandlerRegistryGenerated.Build());
//or provide a manual registration method
//builder.Services.AddServiceLib();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //https://localhost:7016/openapi/v1.json
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

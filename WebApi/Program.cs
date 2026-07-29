using MediatorLib;
using MediatorLib.Mediator;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Enable Swagger/OpenAPI UI in development
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//use the source generator to produce a static registry and register it
builder.Services.AddMediator(ServiceLib.HandlerRegistryGenerated.Build());
//or just let DI do it for you
//builder.Services.AddMediator();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //https://localhost:7016/openapi/v1.json
    // register the generated OpenAPI document endpoint
    app.MapOpenApi();
    // expose Swagger UI at /swagger
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1"));
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

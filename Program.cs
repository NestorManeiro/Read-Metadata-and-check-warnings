using System.Net;
using DeepFakeDetector.Services;
using DeepFakeDetector.Services.Interfaces;
using DeepFakeDetector.Models.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurar la sección TechnicalValidation del appsettings.json
builder.Services.Configure<TechnicalValidationConfig>(
    builder.Configuration.GetSection("TechnicalValidation"));

// 2. Registrar el validador técnico como singleton
builder.Services.AddSingleton<TechnicalValidator>();

// 3. Configurar Kestrel
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Listen(IPAddress.Any, 5283);
    serverOptions.Listen(IPAddress.Any, 7292, listenOptions =>
    {
        listenOptions.UseHttps();
    });
});

// 4. Configurar servicios principales
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 5. Registrar ExifService con inyección de IOptions
builder.Services.AddScoped<IExifService, ExifService>();


// 6. Configurar CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// 7. Configurar pipeline de middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("AllowAll");
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

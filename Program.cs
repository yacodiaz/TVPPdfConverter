using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Facturas API", Version = "v1" });
});

// Pdf discovery defaults
builder.Services.AddSingleton(new TVPPdfConverter.Services.Discovery.PdfDiscoveryService());

// Configurar forwarded headers para Railway
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Aumentar el límite de tamaño de archivos para uploads grandes
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // 100 MB
});
// Escuchar el puerto inyectado por la plataforma (Render/otros) o 8080 por defecto
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");
}
else
{
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}

var app = builder.Build();

// Configurar forwarded headers (importante para Railway)
app.UseForwardedHeaders();

// Habilitar Swagger tanto en Development como en Production para Railway
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Facturas API V1");
    c.RoutePrefix = "swagger"; // Esto hace que esté disponible en /swagger
});

// Agregar un endpoint simple de health check
app.MapGet("/health", () => "OK");

// Servir UI estática desde wwwroot (index.html en /)
app.UseDefaultFiles();
app.UseStaticFiles();           // habilita wwwroot
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

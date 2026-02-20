using AnLar.HtmlToPdf.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<AccessiblePdfGenerator>();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

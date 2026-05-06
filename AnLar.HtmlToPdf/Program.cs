using AnLar.HtmlToPdf.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Singleton: AccessiblePdfGenerator holds no per-request state and the cached
// FontProvider should outlive a single request.
builder.Services.AddSingleton<AccessiblePdfGenerator>();

var app = builder.Build();

// Pre-warm iText (CSS parser, font scan) on a background thread so the first
// real request doesn't pay the ~20s cold-start cost.
var generator = app.Services.GetRequiredService<AccessiblePdfGenerator>();
_ = Task.Run(generator.Warmup);

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

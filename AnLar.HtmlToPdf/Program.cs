using AnLar.HtmlToPdf.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Callers ship large HTML payloads (hundreds of KB of text) — accept
// Content-Encoding: gzip/br/deflate request bodies on all endpoints.
// Plain requests are untouched; the decompressed size is still enforced
// against MaxRequestBodySize, so this does not open a zip-bomb hole.
builder.Services.AddRequestDecompression();
// Singleton: AccessiblePdfGenerator holds no per-request state and the cached
// FontProvider should outlive a single request.
builder.Services.AddSingleton<AccessiblePdfGenerator>();

var app = builder.Build();

// Pre-warm iText (CSS parser, font scan) on a background thread so the first
// real request doesn't pay the ~20s cold-start cost.
var generator = app.Services.GetRequiredService<AccessiblePdfGenerator>();
_ = Task.Run(generator.Warmup);

app.UseHttpsRedirection();
app.UseRequestDecompression();
app.MapControllers();

app.Run();

// Exposes the implicit Program class to WebApplicationFactory-based integration tests.
public partial class Program { }

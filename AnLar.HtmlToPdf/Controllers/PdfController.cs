using AnLar.HtmlToPdf.DTOs;
using AnLar.HtmlToPdf.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AnLar.HtmlToPdf.Controllers
{
    [ApiController]
    public class PdfController : ControllerBase
    {
        // JsonSerializerOptions caches type metadata internally — reuse one
        // instance instead of rebuilding that cache on every streaming request.
        private static readonly JsonSerializerOptions NdjsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private static readonly byte[] NdjsonNewline = [(byte)'\n'];

        private readonly AccessiblePdfGenerator _pdfGenerator;

        public PdfController(AccessiblePdfGenerator pdfGenerator)
        {
            _pdfGenerator = pdfGenerator;
        }

        [HttpPost("/pdf")]
        public async Task<IActionResult> GeneratePdf([FromBody] PdfRequest request, CancellationToken cancellationToken)
        {
            var validationError = ValidateRequest(request);
            if (validationError != null)
                return validationError;

            var orientation = request.PageOrientation?.ToLowerInvariant() ?? "portrait";

            // PDF generation is CPU-bound and synchronous in iText. Off-load it so
            // the request thread isn't tied up for the duration of a long render.
            var pdfBytes = await Task.Run(() => _pdfGenerator.GenerateAccessiblePdfFromHtml(
                request.HtmlContent,
                request.DocumentTitle ?? "Untitled",
                request.DocumentLanguage ?? "en-US",
                orientation,
                request.MarginTop ?? 10f,
                request.MarginRight ?? 10f,
                request.MarginBottom ?? 10f,
                request.MarginLeft ?? 10f,
                request.ShowPageNumbers ?? false,
                request.Watermark,
                request.FooterContent,
                request.Accessible ?? true), cancellationToken);

            return File(pdfBytes, "application/pdf");
        }

        [HttpPost("/pdf/images")]
        public async Task<IActionResult> GeneratePdfImages([FromBody] PdfRequest request, CancellationToken cancellationToken)
        {
            var validationError = ValidateRequest(request);
            if (validationError != null)
                return validationError;

            var orientation = request.PageOrientation?.ToLowerInvariant() ?? "portrait";

            var response = await Task.Run(() => _pdfGenerator.GeneratePdfPageImages(
                request.HtmlContent,
                request.DocumentTitle ?? "Untitled",
                request.DocumentLanguage ?? "en-US",
                orientation,
                request.MarginTop ?? 10f,
                request.MarginRight ?? 10f,
                request.MarginBottom ?? 10f,
                request.MarginLeft ?? 10f,
                request.ShowPageNumbers ?? false,
                request.Watermark,
                request.Dpi ?? 300,
                request.FooterContent,
                // Output is a rasterized PNG — the tag tree is pure waste here, so
                // default to the fast (untagged) path unless explicitly requested.
                request.Accessible ?? false), cancellationToken);

            return Ok(response);
        }

        /// <summary>
        /// Streaming variant of /pdf/images for large jobs. Emits NDJSON
        /// (one PageImage JSON object per line) and flushes after every page,
        /// so server memory stays bounded regardless of total page count.
        /// </summary>
        [HttpPost("/pdf/images/stream")]
        public async Task GeneratePdfImagesStream([FromBody] PdfRequest request, CancellationToken cancellationToken)
        {
            var validationError = ValidateRequest(request);
            if (validationError != null)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new { error = validationError.Value }, cancellationToken);
                return;
            }

            var orientation = request.PageOrientation?.ToLowerInvariant() ?? "portrait";

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/x-ndjson";
            // Disable response buffering so per-page flushes actually leave the box.
            Response.Headers.CacheControl = "no-cache";
            var bufferingFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            var pages = _pdfGenerator.GeneratePdfPageImagesStreamAsync(
                request.HtmlContent,
                request.DocumentTitle ?? "Untitled",
                request.DocumentLanguage ?? "en-US",
                orientation,
                request.MarginTop ?? 10f,
                request.MarginRight ?? 10f,
                request.MarginBottom ?? 10f,
                request.MarginLeft ?? 10f,
                request.ShowPageNumbers ?? false,
                request.Watermark,
                request.Dpi ?? 300,
                request.FooterContent,
                // Raster output — default to the fast (untagged) path.
                request.Accessible ?? false,
                cancellationToken);

            await foreach (var page in pages.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(Response.Body, page, NdjsonSerializerOptions, cancellationToken);
                await Response.Body.WriteAsync(NdjsonNewline, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }

        private BadRequestObjectResult? ValidateRequest(PdfRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.HtmlContent))
                return BadRequest("htmlContent is required.");

            var orientation = request.PageOrientation?.ToLowerInvariant();
            if (orientation != null && orientation != "portrait" && orientation != "landscape")
                return BadRequest("pageOrientation must be \"portrait\" or \"landscape\".");

            return null;
        }
    }
}

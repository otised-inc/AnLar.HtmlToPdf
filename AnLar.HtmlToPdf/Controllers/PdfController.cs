using AnLar.HtmlToPdf.DTOs;
using AnLar.HtmlToPdf.Services;
using Microsoft.AspNetCore.Mvc;

namespace AnLar.HtmlToPdf.Controllers
{
    [ApiController]
    public class PdfController : ControllerBase
    {
        private readonly AccessiblePdfGenerator _pdfGenerator;

        public PdfController(AccessiblePdfGenerator pdfGenerator)
        {
            _pdfGenerator = pdfGenerator;
        }

        [HttpPost("/pdf")]
        public IActionResult GeneratePdf([FromBody] PdfRequest request)
        {
            var validationError = ValidateRequest(request);
            if (validationError != null)
                return validationError;

            var orientation = request.PageOrientation?.ToLowerInvariant() ?? "portrait";

            var pdfBytes = _pdfGenerator.GenerateAccessiblePdfFromHtml(
                request.HtmlContent,
                request.DocumentTitle ?? "Untitled",
                request.DocumentLanguage ?? "en-US",
                orientation,
                request.MarginTop ?? 10f,
                request.MarginRight ?? 10f,
                request.MarginBottom ?? 10f,
                request.MarginLeft ?? 10f,
                request.ShowPageNumbers ?? false,
                request.Watermark);

            return File(pdfBytes, "application/pdf");
        }

        [HttpPost("/pdf/images")]
        public IActionResult GeneratePdfImages([FromBody] PdfRequest request)
        {
            var validationError = ValidateRequest(request);
            if (validationError != null)
                return validationError;

            var orientation = request.PageOrientation?.ToLowerInvariant() ?? "portrait";

            var response = _pdfGenerator.GeneratePdfPageImages(
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
                request.Dpi ?? 300);

            return Ok(response);
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

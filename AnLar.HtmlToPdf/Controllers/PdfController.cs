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
            if (string.IsNullOrWhiteSpace(request.HtmlContent))
                return BadRequest("htmlContent is required.");

            var pdfBytes = _pdfGenerator.GenerateAccessiblePdfFromHtml(
                request.HtmlContent,
                request.DocumentTitle ?? "Untitled",
                request.DocumentLanguage ?? "en-US");

            return File(pdfBytes, "application/pdf");
        }
    }
}

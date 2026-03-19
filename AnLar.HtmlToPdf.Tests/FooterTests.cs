using AnLar.HtmlToPdf.Services;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    public class FooterTests
    {
        private readonly AccessiblePdfGenerator _generator;

        public FooterTests()
        {
            var logger = NullLoggerFactory.Instance.CreateLogger<AccessiblePdfGenerator>();
            _generator = new AccessiblePdfGenerator(logger);
        }

        [Fact]
        public void GeneratePdf_WithFooterContent_ProducesValidPdf()
        {
            var html = "<h1>Test Document</h1><p>Body content here.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Footer Test",
                footerContent: "<p style='text-align:center; font-size:8pt;'>Company Confidential</p>");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged(), "PDF should be tagged for accessibility");
            Assert.True(pdfDoc.GetNumberOfPages() >= 1);
        }

        [Fact]
        public void GeneratePdf_WithStyledFooterContent_ProducesValidPdf()
        {
            var html = "<h1>Styled Footer Test</h1><p>Body content.</p>";
            var footer = @"<div style='border-top:1px solid #ccc; padding-top:4px; font-size:7pt; color:#666;'>
                <table style='width:100%'><tr>
                    <td style='text-align:left'>Left text</td>
                    <td style='text-align:center'>Center text</td>
                    <td style='text-align:right'>Right text</td>
                </tr></table>
            </div>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Styled Footer Test", footerContent: footer);

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());
        }

        [Fact]
        public void GeneratePdf_WithFooterAndPageNumbers_ProducesValidPdf()
        {
            var html = "<h1>Footer + Page Numbers</h1><p>Body content.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Footer PageNum Test",
                showPageNumbers: true,
                footerContent: "<p style='text-align:right; font-size:8pt;'>My Footer</p>");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());
            Assert.True(pdfDoc.GetNumberOfPages() >= 1);
        }

        [Fact]
        public void GeneratePdf_WithFooterAndWatermark_ProducesValidPdf()
        {
            var html = "<h1>Footer + Watermark</h1><p>Body content.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Footer Watermark Test",
                watermark: "DRAFT",
                footerContent: "<p style='font-size:8pt;'>Draft Document</p>");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());
        }

        [Fact]
        public void GeneratePdf_WithNoFooterContent_MatchesOriginalBehavior()
        {
            var html = "<h1>No Footer</h1><p>Body content.</p>";

            var withNull = _generator.GenerateAccessiblePdfFromHtml(html, "No Footer Test", footerContent: null);
            var withEmpty = _generator.GenerateAccessiblePdfFromHtml(html, "No Footer Test", footerContent: "");
            var withoutParam = _generator.GenerateAccessiblePdfFromHtml(html, "No Footer Test");

            // All three should produce valid PDFs of similar size (no footer stamped)
            Assert.True(withNull.Length > 0);
            Assert.True(withEmpty.Length > 0);
            Assert.True(withoutParam.Length > 0);

            // Without footer, no stamping pass occurs, so sizes should be identical
            Assert.Equal(withNull.Length, withoutParam.Length);
            Assert.Equal(withEmpty.Length, withoutParam.Length);
        }
    }
}

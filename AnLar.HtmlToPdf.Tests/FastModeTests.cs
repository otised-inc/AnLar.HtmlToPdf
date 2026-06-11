using System.Text;
using AnLar.HtmlToPdf.Services;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    public class FastModeTests
    {
        private readonly AccessiblePdfGenerator _generator;

        public FastModeTests()
        {
            var logger = new NullLogger<AccessiblePdfGenerator>();
            _generator = new AccessiblePdfGenerator(logger);
        }

        [Fact]
        public void GeneratePdf_AccessibleFalse_ProducesUntaggedPdf()
        {
            var html = "<h1>Fast Mode</h1><p>Body content here.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Fast Mode Test", accessible: false);

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.False(pdfDoc.IsTagged(), "Fast-mode PDF should NOT be tagged");
            Assert.True(pdfDoc.GetNumberOfPages() >= 1);
        }

        [Fact]
        public void GeneratePdf_AccessibleTrue_ProducesTaggedPdf()
        {
            var html = "<h1>Accessible Mode</h1><p>Body content here.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Accessible Mode Test", accessible: true);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged(), "Accessible PDF should be tagged");
        }

        [Fact]
        public void GeneratePdf_AccessibleDefault_IsTagged()
        {
            var html = "<h1>Default Mode</h1><p>Body content here.</p>";

            // Omitting the flag must default to the accessible (tagged) path.
            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Default Mode Test");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged(), "Default behavior should remain accessible (tagged)");
        }

        [Fact]
        public void GeneratePdf_FastMode_WithStampingFeatures_ProducesValidPdf()
        {
            var html = "<h1>Fast + Stamping</h1><p>Body content here.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Fast Stamping Test",
                showPageNumbers: true,
                watermark: "DRAFT",
                footerContent: "<p style='text-align:center; font-size:8pt;'>Confidential</p>",
                accessible: false);

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.False(pdfDoc.IsTagged(), "Fast-mode PDF should NOT be tagged even with stamping");
            Assert.True(pdfDoc.GetNumberOfPages() >= 1);
        }

        [Fact]
        public void GeneratePdf_FastMode_NoPdfUaMetadata()
        {
            var html = "<h1>Fast Mode</h1><p>Body content here.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Fast Mode Test", accessible: false);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            var xmpBytes = pdfDoc.GetXmpMetadataBytes();
            if (xmpBytes != null)
            {
                var xmp = Encoding.UTF8.GetString(xmpBytes);
                Assert.DoesNotContain("pdfuaid", xmp);
            }
            // No XMP metadata at all is also acceptable for fast mode.
        }
    }
}

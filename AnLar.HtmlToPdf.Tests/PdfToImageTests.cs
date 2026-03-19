using AnLar.HtmlToPdf.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    public class PdfToImageTests
    {
        private readonly AccessiblePdfGenerator _generator;

        public PdfToImageTests()
        {
            var logger = NullLoggerFactory.Instance.CreateLogger<AccessiblePdfGenerator>();
            _generator = new AccessiblePdfGenerator(logger);
        }

        [Fact]
        public void GeneratePdfPageImages_SinglePage_ReturnsOneImage()
        {
            var html = "<h1>Hello World</h1><p>Single page content.</p>";

            var result = _generator.GeneratePdfPageImages(html, "Single Page Test");

            Assert.Equal(1, result.PageCount);
            Assert.Single(result.Pages);
        }

        [Fact]
        public void GeneratePdfPageImages_ReturnsValidBase64Png()
        {
            var html = "<h1>Test</h1><p>Content</p>";

            var result = _generator.GeneratePdfPageImages(html, "PNG Test");

            Assert.NotEmpty(result.Pages);
            var imageBytes = Convert.FromBase64String(result.Pages[0]);
            // PNG magic bytes: 137 80 78 71 13 10 26 10
            Assert.True(imageBytes.Length > 8, "Image should have meaningful content");
            Assert.Equal(0x89, imageBytes[0]);
            Assert.Equal((byte)'P', imageBytes[1]);
            Assert.Equal((byte)'N', imageBytes[2]);
            Assert.Equal((byte)'G', imageBytes[3]);
        }

        [Fact]
        public void GeneratePdfPageImages_MultiPage_ReturnsCorrectCount()
        {
            // Generate enough content to span multiple pages
            var paragraphs = string.Join("\n",
                Enumerable.Range(1, 80).Select(i => $"<p>Paragraph {i} with enough text to take up space on the page.</p>"));
            var html = $"<h1>Multi-Page Document</h1>{paragraphs}";

            var result = _generator.GeneratePdfPageImages(html, "Multi Page Test");

            Assert.True(result.PageCount > 1, $"Expected multiple pages but got {result.PageCount}");
            Assert.Equal(result.PageCount, result.Pages.Count);

            // Verify every page is a valid base64 PNG
            foreach (var page in result.Pages)
            {
                var bytes = Convert.FromBase64String(page);
                Assert.Equal(0x89, bytes[0]);
                Assert.Equal((byte)'P', bytes[1]);
            }
        }

        [Fact]
        public void GeneratePdfPageImages_CustomDpi_ProducesDifferentSizedImages()
        {
            var html = "<h1>DPI Test</h1><p>Content</p>";

            var lowDpi = _generator.GeneratePdfPageImages(html, "DPI Test", dpi: 72);
            var highDpi = _generator.GeneratePdfPageImages(html, "DPI Test", dpi: 300);

            var lowBytes = Convert.FromBase64String(lowDpi.Pages[0]);
            var highBytes = Convert.FromBase64String(highDpi.Pages[0]);

            Assert.True(highBytes.Length > lowBytes.Length,
                $"300 DPI image ({highBytes.Length} bytes) should be larger than 72 DPI image ({lowBytes.Length} bytes)");
        }

        [Fact]
        public void GeneratePdfPageImages_WithWatermark_ProducesImages()
        {
            var html = "<h1>Watermark Test</h1><p>Content with watermark.</p>";

            var result = _generator.GeneratePdfPageImages(
                html, "Watermark Test", watermark: "DRAFT");

            Assert.Equal(1, result.PageCount);
            Assert.Single(result.Pages);
            var imageBytes = Convert.FromBase64String(result.Pages[0]);
            Assert.True(imageBytes.Length > 0, "Image with watermark should not be empty");
        }

        [Fact]
        public void GeneratePdfPageImages_WithPageNumbers_ProducesImages()
        {
            var html = "<h1>Page Numbers Test</h1><p>Content with page numbers.</p>";

            var result = _generator.GeneratePdfPageImages(
                html, "Page Numbers Test", showPageNumbers: true);

            Assert.Equal(1, result.PageCount);
            Assert.Single(result.Pages);
        }

        [Fact]
        public void GeneratePdfPageImages_LandscapeOrientation_ProducesImages()
        {
            var html = "<h1>Landscape Test</h1><p>Content in landscape.</p>";

            var result = _generator.GeneratePdfPageImages(
                html, "Landscape Test", pageOrientation: "landscape");

            Assert.Equal(1, result.PageCount);
            Assert.Single(result.Pages);
            var imageBytes = Convert.FromBase64String(result.Pages[0]);
            Assert.True(imageBytes.Length > 0);
        }
    }
}

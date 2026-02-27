using AnLar.HtmlToPdf.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    public class InlineImageTests
    {
        private readonly AccessiblePdfGenerator _generator;

        // 1x1 red pixel PNG encoded as base64
        private const string Base64Png1x1 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==";

        // Minimal valid JPEG (1x1 white pixel) encoded as base64
        private const string Base64Jpeg1x1 =
            "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAs" +
            "LDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zND" +
            "L/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIy" +
            "MjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAx" +
            "EB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/" +
            "xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/a" +
            "AAwDAQACEQMRAD8AVQD/2Q==";

        public InlineImageTests()
        {
            var logger = NullLoggerFactory.Instance.CreateLogger<AccessiblePdfGenerator>();
            _generator = new AccessiblePdfGenerator(logger);
        }

        [Fact]
        public void GeneratePdf_WithBase64PngImage_ProducesValidPdf()
        {
            var html = $@"<h1>Test Document</h1>
                <p>Below is an inline PNG image:</p>
                <img src=""data:image/png;base64,{Base64Png1x1}"" alt=""A red pixel"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Test PDF with PNG");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0, "PDF should not be empty");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged(), "PDF should be tagged for accessibility");
            Assert.True(pdfDoc.GetNumberOfPages() >= 1, "PDF should have at least one page");
        }

        [Fact]
        public void GeneratePdf_WithBase64JpegImage_ProducesValidPdf()
        {
            var html = $@"<h1>Test Document</h1>
                <img src=""data:image/jpeg;base64,{Base64Jpeg1x1}"" alt=""A white pixel"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Test PDF with JPEG");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0, "PDF should not be empty");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged(), "PDF should be tagged for accessibility");
        }

        [Fact]
        public void GeneratePdf_WithImageAltText_HasFigureWithAltText()
        {
            var html = $@"<img src=""data:image/png;base64,{Base64Png1x1}"" alt=""Test image description"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Alt Text Test");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            Assert.NotNull(structTreeRoot);

            bool foundFigure = FindStructureElement(structTreeRoot, "Figure", out var figureElement);
            Assert.True(foundFigure, "PDF should contain a Figure structure element for the image");

            var altText = figureElement?.GetAlt();
            Assert.NotNull(altText);
            Assert.Equal("Test image description", altText.ToString());
        }

        [Fact]
        public void GeneratePdf_WithMultipleImages_AllAreTaggedCorrectly()
        {
            var html = $@"<h1>Multiple Images</h1>
                <p>First image:</p>
                <img src=""data:image/png;base64,{Base64Png1x1}"" alt=""First image"" />
                <p>Second image:</p>
                <img src=""data:image/png;base64,{Base64Png1x1}"" alt=""Second image"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Multiple Images Test");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var figureCount = CountStructureElements(structTreeRoot, "Figure");
            Assert.Equal(2, figureCount);
        }

        [Fact]
        public void GeneratePdf_WithImageWithinParagraph_ProducesValidPdf()
        {
            var html = $@"<p>Here is an inline image " +
                       $@"<img src=""data:image/png;base64,{Base64Png1x1}"" alt=""inline image"" />" +
                       $@" within text.</p>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Inline Image Test");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());
        }

        [Fact]
        public void GeneratePdf_WithImageNoAltAttribute_GetsFallbackDescription()
        {
            // Image with no alt attribute at all
            var html = $@"<img src=""data:image/png;base64,{Base64Png1x1}"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "No Alt Test");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            bool foundFigure = FindStructureElement(structTreeRoot, "Figure", out var figureElement);
            Assert.True(foundFigure, "Image without alt should still be tagged as Figure");

            // Should have a fallback description for 508 compliance
            var altText = figureElement?.GetAlt();
            Assert.NotNull(altText);
            Assert.False(string.IsNullOrEmpty(altText.ToString()),
                "Should have fallback alt text for 508 compliance");
        }

        [Fact]
        public void GeneratePdf_WithDecorativeImage_IsExcludedFromStructureTree()
        {
            // Decorative image with empty alt=""
            var html = $@"<p>Text content</p>" +
                       $@"<img src=""data:image/png;base64,{Base64Png1x1}"" alt="""" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Decorative Image Test");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            // Decorative image (alt="") should NOT produce a Figure element
            var figureCount = CountStructureElements(structTreeRoot, "Figure");
            Assert.Equal(0, figureCount);
        }

        [Fact]
        public void GeneratePdf_With508ComplianceMetadata_IsFullyAccessible()
        {
            var html = $@"<h1>508 Compliance Test</h1>
                <p>Document with an accessible image:</p>
                <img src=""data:image/png;base64,{Base64Png1x1}"" alt=""Accessible image"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "508 Test", "en-US");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            // PDF must be tagged
            Assert.True(pdfDoc.IsTagged(), "PDF must be tagged for 508 compliance");

            // Language must be set
            var lang = pdfDoc.GetCatalog().GetLang();
            Assert.NotNull(lang);
            Assert.Equal("en-US", lang.ToString());

            // Document title must be set
            var info = pdfDoc.GetDocumentInfo();
            Assert.Equal("508 Test", info.GetTitle());

            // DisplayDocTitle viewer preference must be true
            var viewerPrefs = pdfDoc.GetCatalog().GetViewerPreferences();
            Assert.NotNull(viewerPrefs);

            // Structure tree must exist and contain headings and figure
            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            Assert.NotNull(structTreeRoot);
            Assert.True(FindStructureElement(structTreeRoot, "H1", out _),
                "PDF should have H1 structure element");
            Assert.True(FindStructureElement(structTreeRoot, "Figure", out _),
                "PDF should have Figure structure element for image");
        }

        [Fact]
        public void GeneratePdf_WithImageHavingWidthHeight_ProducesValidPdf()
        {
            var html = $@"<img src=""data:image/png;base64,{Base64Png1x1}"" " +
                       $@"alt=""Sized image"" width=""200"" height=""100"" />";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Sized Image Test");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            bool foundFigure = FindStructureElement(structTreeRoot, "Figure", out var figureElement);
            Assert.True(foundFigure);
            Assert.Equal("Sized image", figureElement?.GetAlt()?.ToString());
        }

        [Fact]
        public void GeneratePdf_WithFullHtmlDocumentContainingImages_ProducesValidPdf()
        {
            // Test with a complete HTML document (not a snippet)
            var html = $@"<!DOCTYPE html>
<html lang=""en-US"">
<head>
    <meta charset=""UTF-8""/>
    <title>Full HTML Test</title>
    <style>
        @page {{ margin: 10mm; }}
        img {{ max-width: 100%; height: auto; }}
    </style>
</head>
<body style=""font-family: 'Liberation Serif', 'Times New Roman', Times, serif; font-size: 16px;"">
    <h1>Full Document Test</h1>
    <p>Image in a full HTML document:</p>
    <img src=""data:image/png;base64,{Base64Png1x1}"" alt=""Full doc image"" />
</body>
</html>";

            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Full HTML Test");

            Assert.NotNull(pdfBytes);
            Assert.True(pdfBytes.Length > 0);

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged());
        }

        /// <summary>
        /// Recursively searches the PDF structure tree for an element with the given role.
        /// </summary>
        private static bool FindStructureElement(
            IStructureNode node, string role, out PdfStructElem? foundElement)
        {
            foundElement = null;

            if (node is PdfStructElem elem)
            {
                var elemRole = elem.GetRole();
                if (elemRole != null && elemRole.GetValue() == role)
                {
                    foundElement = elem;
                    return true;
                }
            }

            var kids = node.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is IStructureNode childNode)
                    {
                        if (FindStructureElement(childNode, role, out foundElement))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Counts structure elements with the specified role anywhere in the tag tree.
        /// </summary>
        private static int CountStructureElements(IStructureNode node, string role)
        {
            int count = 0;

            if (node is PdfStructElem elem)
            {
                var elemRole = elem.GetRole();
                if (elemRole != null && elemRole.GetValue() == role)
                    count++;
            }

            var kids = node.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is IStructureNode childNode)
                    {
                        count += CountStructureElements(childNode, role);
                    }
                }
            }

            return count;
        }
    }
}

using AnLar.HtmlToPdf.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    /// <summary>
    /// Guards the 508/PDF-UA invariants that must survive performance work:
    /// tagging, document metadata, XMP PDF/UA identifier, embedded body fonts,
    /// artifact-marked overlays, and an unaltered structure tree after the
    /// stamping pass.
    /// </summary>
    public class CompliancePreservationTests
    {
        private readonly AccessiblePdfGenerator _generator;

        // 1x1 red pixel PNG encoded as base64
        private const string Base64Png1x1 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==";

        public CompliancePreservationTests()
        {
            var logger = new NullLogger<AccessiblePdfGenerator>();
            _generator = new AccessiblePdfGenerator(logger);
        }

        private static string MultiPageHtml()
        {
            var paragraphs = string.Concat(Enumerable.Repeat(
                "<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>", 80));
            return $"<h1>Compliance Document</h1>{paragraphs}" +
                   $"<img src=\"data:image/png;base64,{Base64Png1x1}\" alt=\"Sample figure\" />";
        }

        [Fact]
        public void StampedPdf_PreservesAccessibilityMetadata()
        {
            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                MultiPageHtml(), "Stamped Compliance Test", "en-US",
                showPageNumbers: true,
                watermark: "DRAFT",
                footerContent: "<p style='font-size:8pt;'>Page {pageNumber} of {totalPages}</p>");

            using var ms = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            Assert.True(pdfDoc.IsTagged(), "Stamped PDF must remain tagged");
            Assert.Equal("en-US", pdfDoc.GetCatalog().GetLang()?.ToString());
            Assert.Equal("Stamped Compliance Test", pdfDoc.GetDocumentInfo().GetTitle());
            Assert.NotNull(pdfDoc.GetCatalog().GetViewerPreferences());

            // XMP must still carry the PDF/UA-1 identifier after stamping
            var xmp = pdfDoc.GetXmpMetadata();
            Assert.NotNull(xmp);
            var part = xmp.GetProperty("http://www.aiim.org/pdfua/ns/id/", "part");
            Assert.NotNull(part);
            Assert.Equal("1", part.GetValue());

            // Structure tree must survive with real content roles
            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            Assert.NotNull(structTreeRoot);
            Assert.True(CountStructureElements(structTreeRoot, "H1") > 0, "H1 must survive stamping");
            Assert.True(CountStructureElements(structTreeRoot, "Figure") > 0, "Figure must survive stamping");

            // Body (Type0/CID) fonts must be embedded on every page
            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                var fonts = pdfDoc.GetPage(i).GetResources()?.GetResource(PdfName.Font);
                if (fonts == null) continue;
                foreach (var key in fonts.KeySet())
                {
                    var fontDict = fonts.GetAsDictionary(key);
                    if (fontDict != null && PdfName.Type0.Equals(fontDict.GetAsName(PdfName.Subtype)))
                    {
                        Assert.True(HasEmbeddedFontFile(fontDict),
                            $"Type0 font on page {i} must be embedded for 508 compliance");
                    }
                }
            }
        }

        [Fact]
        public void Stamping_DoesNotAlterStructureTree_AndMarksOverlaysAsArtifacts()
        {
            var html = MultiPageHtml();

            var plainBytes = _generator.GenerateAccessiblePdfFromHtml(html, "Doc");
            var stampedBytes = _generator.GenerateAccessiblePdfFromHtml(
                html, "Doc",
                showPageNumbers: true,
                watermark: "DRAFT",
                footerContent: "<p style='font-size:8pt;'>Confidential</p>");

            using var plainDoc = new PdfDocument(new PdfReader(new MemoryStream(plainBytes)));
            using var stampedDoc = new PdfDocument(new PdfReader(new MemoryStream(stampedBytes)));

            // The stamping pass must not add, remove, or re-role structure elements
            foreach (var role in new[] { "H1", "P", "Figure" })
            {
                Assert.Equal(
                    CountStructureElements(plainDoc.GetStructTreeRoot(), role),
                    CountStructureElements(stampedDoc.GetStructTreeRoot(), role));
            }

            // Every stamped overlay (watermark, footer, page number) must be inside
            // an Artifact marked-content block so assistive tech ignores it
            for (int i = 1; i <= stampedDoc.GetNumberOfPages(); i++)
            {
                var content = System.Text.Encoding.ASCII.GetString(
                    stampedDoc.GetPage(i).GetContentBytes());
                Assert.Contains("/Artifact", content);
            }
        }

        [Fact]
        public void Footer_TotalPagesOnlyPlaceholder_RendersOnEveryPage()
        {
            // {totalPages} without {pageNumber} takes the shared-elements fast path —
            // verify the substituted value still renders on every page
            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                MultiPageHtml(), "Doc",
                footerContent: "<p style='font-size:8pt;'>Document of {totalPages} pages</p>");

            using var pdfDoc = new PdfDocument(new PdfReader(new MemoryStream(pdfBytes)));
            int totalPages = pdfDoc.GetNumberOfPages();
            Assert.True(totalPages > 1, "Test document should span multiple pages");

            for (int i = 1; i <= totalPages; i++)
            {
                var text = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i));
                Assert.Contains($"Document of {totalPages} pages", text);
            }
        }

        [Fact]
        public void Footer_PageNumberPlaceholder_RendersPerPageValues()
        {
            var pdfBytes = _generator.GenerateAccessiblePdfFromHtml(
                MultiPageHtml(), "Doc",
                footerContent: "<p style='font-size:8pt;'>Page {pageNumber} of {totalPages}</p>");

            using var pdfDoc = new PdfDocument(new PdfReader(new MemoryStream(pdfBytes)));
            int totalPages = pdfDoc.GetNumberOfPages();
            Assert.True(totalPages > 1, "Test document should span multiple pages");

            for (int i = 1; i <= totalPages; i++)
            {
                var text = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i));
                Assert.Contains($"Page {i} of {totalPages}", text);
            }
        }

        private static bool HasEmbeddedFontFile(PdfDictionary fontDict)
        {
            var descriptor = fontDict.GetAsDictionary(PdfName.FontDescriptor);
            if (descriptor == null)
            {
                var descendants = fontDict.GetAsArray(PdfName.DescendantFonts);
                descriptor = descendants?.GetAsDictionary(0)?.GetAsDictionary(PdfName.FontDescriptor);
            }
            if (descriptor == null)
                return false;
            return descriptor.ContainsKey(PdfName.FontFile)
                || descriptor.ContainsKey(PdfName.FontFile2)
                || descriptor.ContainsKey(PdfName.FontFile3);
        }

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
                        count += CountStructureElements(childNode, role);
                }
            }

            return count;
        }
    }
}

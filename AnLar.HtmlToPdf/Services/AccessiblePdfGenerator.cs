using AnLar.HtmlToPdf.DTOs;
using iText.Html2pdf;
using iText.Html2pdf.Attach;
using iText.Html2pdf.Attach.Impl;
using iText.Html2pdf.Attach.Impl.Tags;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Colors;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.XMP;
using iText.Kernel.Geom;
using iText.Layout;
using iText.Layout.Font;
using iText.StyledXmlParser.Node;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AnLar.HtmlToPdf.Services
{
    public class AccessiblePdfGenerator
    {
        private readonly ILogger<AccessiblePdfGenerator> _logger;

        public AccessiblePdfGenerator(ILogger<AccessiblePdfGenerator> logger)
        {
            _logger = logger;
        }

        public byte[] GenerateAccessiblePdfFromHtml(
            string htmlContent,
            string documentTitle,
            string documentLanguage = "en-US",
            string pageOrientation = "portrait",
            float marginTop = 10f,
            float marginRight = 10f,
            float marginBottom = 10f,
            float marginLeft = 10f,
            bool showPageNumbers = false,
            string? watermark = null,
            string? footerContent = null)
        {
            using var memoryStream = new MemoryStream();

            var writerProperties = new WriterProperties();
            writerProperties.SetPdfVersion(PdfVersion.PDF_1_7);

            using (var pdfWriter = new PdfWriter(memoryStream, writerProperties))
            using (var pdfDocument = new PdfDocument(pdfWriter))
            {
                pdfDocument.SetTagged();
                pdfDocument.GetCatalog().SetLang(new PdfString(documentLanguage));
                pdfDocument.GetCatalog().SetViewerPreferences(
                    new PdfViewerPreferences().SetDisplayDocTitle(true));

                var documentInfo = pdfDocument.GetDocumentInfo();
                documentInfo.SetTitle(documentTitle);

                SetPdfUaXmpMetadata(pdfDocument, documentTitle);

                var fontProvider = new FontProvider();
                AddBundledFonts(fontProvider);
                foreach (var dir in GetSystemFontDirectories())
                {
                    _logger.LogInformation("Adding system font directory: {Dir}", dir);
                    fontProvider.AddDirectory(dir);
                }

                var converterProperties = new ConverterProperties();
                converterProperties.SetFontProvider(fontProvider);
                converterProperties.SetTagWorkerFactory(new AccessibleTagWorkerFactory());
                converterProperties.SetOutlineHandler(OutlineHandler.CreateStandardHandler());

                // Ensure bottom margin has room for footer content
                bool hasFooter = !string.IsNullOrWhiteSpace(footerContent);
                if (hasFooter && marginBottom < 20f)
                    marginBottom = 20f;

                var wrappedHtml = WrapInHtmlDocument(htmlContent, documentTitle, documentLanguage, pageOrientation, marginTop, marginRight, marginBottom, marginLeft);

                HtmlConverter.ConvertToPdf(wrappedHtml, pdfDocument, converterProperties);
                // HtmlConverter closes the PdfDocument after conversion
            }

            bool needsStamping = showPageNumbers || !string.IsNullOrWhiteSpace(watermark) || !string.IsNullOrWhiteSpace(footerContent);

            if (!needsStamping)
                return memoryStream.ToArray();

            // Build a separate ConverterProperties for the footer HTML (reuses the same font provider).
            // This must be created outside the stamping block since the main converterProperties
            // was scoped to the first pass.
            ConverterProperties? footerConverterProperties = null;
            if (!string.IsNullOrWhiteSpace(footerContent))
            {
                var footerFontProvider = new FontProvider();
                AddBundledFonts(footerFontProvider);
                foreach (var dir in GetSystemFontDirectories())
                    footerFontProvider.AddDirectory(dir);
                footerConverterProperties = new ConverterProperties();
                footerConverterProperties.SetFontProvider(footerFontProvider);
            }

            // Second pass: stamp page numbers, watermark, and/or footer onto the already-generated PDF.
            // A new PdfDocument in stamper mode (reader+writer) must be used because
            // HtmlConverter closes the original document before we can query page count.
            using var inputStream = new MemoryStream(memoryStream.ToArray());
            using var outputStream = new MemoryStream();
            using (var reader = new PdfReader(inputStream))
            using (var stampWriter = new PdfWriter(outputStream))
            using (var stampDoc = new PdfDocument(reader, stampWriter))
            {
                if (!string.IsNullOrWhiteSpace(watermark))
                    AddWatermark(stampDoc, watermark!);
                if (!string.IsNullOrWhiteSpace(footerContent))
                    AddFooter(stampDoc, footerContent!, footerConverterProperties!);
                if (showPageNumbers)
                    AddPageNumbers(stampDoc);
            }

            return outputStream.ToArray();
        }

        public PdfImagesResponse GeneratePdfPageImages(
            string htmlContent,
            string documentTitle,
            string documentLanguage = "en-US",
            string pageOrientation = "portrait",
            float marginTop = 10f,
            float marginRight = 10f,
            float marginBottom = 10f,
            float marginLeft = 10f,
            bool showPageNumbers = false,
            string? watermark = null,
            int dpi = 300)
        {
            var pdfBytes = GenerateAccessiblePdfFromHtml(
                htmlContent, documentTitle, documentLanguage, pageOrientation,
                marginTop, marginRight, marginBottom, marginLeft,
                showPageNumbers, watermark);

            var renderOptions = new RenderOptions { Dpi = dpi };
            var response = new PdfImagesResponse();

            using var pdfStream = new MemoryStream(pdfBytes);
            foreach (var bitmap in Conversion.ToImages(pdfStream, options: renderOptions))
            {
                using (bitmap)
                using (var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100))
                {
                    response.Pages.Add(Convert.ToBase64String(encoded.ToArray()));
                }
            }

            response.PageCount = response.Pages.Count;
            return response;
        }

        private static void AddPageNumbers(PdfDocument pdfDocument)
        {
            int totalPages = pdfDocument.GetNumberOfPages();
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            const float fontSize = 9f;

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageSize = page.GetPageSize();
                var text = $"Page {i} of {totalPages}";
                float textWidth = font.GetWidth(text, fontSize);
                float x = (pageSize.GetWidth() - textWidth) / 2f;

                var canvas = new PdfCanvas(page);
                // Mark as artifact so screen readers and PDF/UA validators ignore it
                canvas.BeginMarkedContent(PdfName.Artifact);
                canvas.SetFontAndSize(font, fontSize)
                      .BeginText()
                      .MoveText(x, 10f)
                      .ShowText(text)
                      .EndText();
                canvas.EndMarkedContent();
                canvas.Release();
            }
        }

        private static void AddFooter(PdfDocument pdfDocument, string footerHtml, ConverterProperties converterProperties)
        {
            int totalPages = pdfDocument.GetNumberOfPages();
            bool hasPlaceholders = footerHtml.Contains("{pageNumber}") || footerHtml.Contains("{totalPages}");

            // If no placeholders, convert once and reuse for all pages
            var sharedElements = hasPlaceholders ? null : HtmlConverter.ConvertToElements(footerHtml, converterProperties);

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageSize = page.GetPageSize();

                // Footer rectangle: full width with 36pt (0.5in) horizontal margins,
                // positioned at the bottom of the page in the margin area.
                const float horizontalMargin = 36f;
                const float footerHeight = 50f;
                const float footerBottom = 10f;
                var footerRect = new Rectangle(
                    pageSize.GetLeft() + horizontalMargin,
                    pageSize.GetBottom() + footerBottom,
                    pageSize.GetWidth() - 2 * horizontalMargin,
                    footerHeight);

                // Replace placeholders per-page if present
                var elements = sharedElements ?? HtmlConverter.ConvertToElements(
                    footerHtml
                        .Replace("{pageNumber}", i.ToString())
                        .Replace("{totalPages}", totalPages.ToString()),
                    converterProperties);

                var pdfCanvas = new PdfCanvas(page);
                // Mark as artifact so screen readers and PDF/UA validators ignore it
                pdfCanvas.BeginMarkedContent(PdfName.Artifact);

                using (var layoutCanvas = new Canvas(pdfCanvas, footerRect))
                {
                    foreach (var element in elements)
                    {
                        if (element is iText.Layout.Element.IBlockElement blockElement)
                            layoutCanvas.Add(blockElement);
                        else if (element is iText.Layout.Element.Image image)
                            layoutCanvas.Add(image);
                    }
                }

                pdfCanvas.EndMarkedContent();
                pdfCanvas.Release();
            }
        }

        private static void AddWatermark(PdfDocument pdfDocument, string watermarkText)
        {
            int totalPages = pdfDocument.GetNumberOfPages();
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            const float fontSize = 60f;
            var grayColor = new DeviceRgb(200, 200, 200);
            var gs = new PdfExtGState().SetFillOpacity(0.3f);

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageSize = page.GetPageSize();
                float centerX = pageSize.GetWidth() / 2f;
                float centerY = pageSize.GetHeight() / 2f;

                // Offset by half the text width so the watermark is visually centered
                float textWidth = font.GetWidth(watermarkText, fontSize);
                float cos = (float)Math.Cos(Math.PI / 4);
                float sin = (float)Math.Sin(Math.PI / 4);
                float x = centerX - (textWidth / 2f) * cos;
                float y = centerY - (textWidth / 2f) * sin;

                var canvas = new PdfCanvas(page.NewContentStreamBefore(), page.GetResources(), pdfDocument);
                canvas.SaveState();
                canvas.SetExtGState(gs);
                // Mark as artifact so screen readers ignore the watermark
                canvas.BeginMarkedContent(PdfName.Artifact);
                canvas.SetFontAndSize(font, fontSize)
                      .SetColor(grayColor, true)
                      .BeginText()
                      .SetTextMatrix(cos, sin, -sin, cos, x, y)
                      .ShowText(watermarkText)
                      .EndText();
                canvas.EndMarkedContent();
                canvas.RestoreState();
                canvas.Release();
            }
        }

        private void AddBundledFonts(FontProvider fontProvider)
        {
            int fontsAdded = 0;

            // Strategy 1: Try Content files from known deployment paths
            var assembly = typeof(AccessiblePdfGenerator).Assembly;
            var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? "";
            string[] candidateDirs =
            [
                Path.Combine(AppContext.BaseDirectory, "Fonts"),
                Path.Combine(assemblyDir, "Fonts"),
                Path.Combine(Directory.GetCurrentDirectory(), "Fonts"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts"),
                // Azure Web App common paths
                "/home/site/wwwroot/Fonts",
            ];

            foreach (var dir in candidateDirs)
            {
                if (!Directory.Exists(dir))
                {
                    _logger.LogDebug("Font dir not found: {Dir}", dir);
                    continue;
                }

                var ttfFiles = Directory.GetFiles(dir, "*.ttf");
                if (ttfFiles.Length == 0)
                    continue;

                _logger.LogInformation("Found {Count} fonts in: {Dir}", ttfFiles.Length, dir);
                fontProvider.AddDirectory(dir);
                fontsAdded += ttfFiles.Length;
                break; // Found fonts, no need to check other paths
            }

            // Strategy 2: Try embedded resources extracted to temp dir
            if (fontsAdded == 0)
            {
                _logger.LogInformation("No Content font files found, trying embedded resources...");
                fontsAdded = ExtractAndAddEmbeddedFonts(fontProvider);
            }

            _logger.LogInformation("Total bundled fonts added: {Count}", fontsAdded);
        }

        private int ExtractAndAddEmbeddedFonts(FontProvider fontProvider)
        {
            var assembly = typeof(AccessiblePdfGenerator).Assembly;
            var allResources = assembly.GetManifestResourceNames();
            var ttfResources = allResources.Where(r => r.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).ToArray();

            _logger.LogInformation(
                "Assembly: {Name}, location: {Location}, total resources: {Total}, ttf resources: {Ttf}",
                assembly.GetName().Name, assembly.Location, allResources.Length, ttfResources.Length);

            if (ttfResources.Length == 0)
                return 0;

            var tempDir = Path.Combine(Path.GetTempPath(), "AnLar.HtmlToPdf.Fonts");
            Directory.CreateDirectory(tempDir);

            int count = 0;
            foreach (var resourceName in ttfResources)
            {
                var targetPath = Path.Combine(tempDir, resourceName);
                if (!File.Exists(targetPath))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                    {
                        _logger.LogWarning("GetManifestResourceStream returned null for {Resource}", resourceName);
                        continue;
                    }
                    using var fs = File.Create(targetPath);
                    stream.CopyTo(fs);
                }
                fontProvider.AddFont(targetPath);
                count++;
                _logger.LogInformation("Loaded embedded font: {Resource} ({Size} bytes)", resourceName, new FileInfo(targetPath).Length);
            }

            return count;
        }

        private static string[] GetSystemFontDirectories()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                return string.IsNullOrEmpty(winFonts) ? [] : [winFonts];
            }

            // Linux font directories (Azure Web Apps, containers, etc.)
            string[] linuxCandidates = ["/usr/share/fonts", "/usr/local/share/fonts", "/home/.fonts"];
            var existing = new System.Collections.Generic.List<string>();
            foreach (var dir in linuxCandidates)
            {
                if (Directory.Exists(dir))
                    existing.Add(dir);
            }
            return [.. existing];
        }

        private static void SetPdfUaXmpMetadata(PdfDocument pdfDocument, string documentTitle)
        {
            XMPMeta xmpMeta = pdfDocument.GetXmpMetadata() ?? XMPMetaFactory.Create();

            const string pdfUaIdSchema = "http://www.aiim.org/pdfua/ns/id/";
            const string pdfUaIdPrefix = "pdfuaid";
            XMPMetaFactory.GetSchemaRegistry().RegisterNamespace(pdfUaIdSchema, pdfUaIdPrefix);

            xmpMeta.SetPropertyInteger(pdfUaIdSchema, "part", 1);

            const string dcSchema = "http://purl.org/dc/elements/1.1/";
            xmpMeta.SetLocalizedText(dcSchema, "title", "x-default", "x-default", documentTitle);

            pdfDocument.SetXmpMetadata(xmpMeta);
        }

        private static string WrapInHtmlDocument(
            string htmlContent,
            string documentTitle,
            string documentLanguage,
            string pageOrientation,
            float marginTop,
            float marginRight,
            float marginBottom,
            float marginLeft)
        {
            if (htmlContent.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || htmlContent.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return htmlContent;
            }

            var orientation = pageOrientation.Equals("landscape", StringComparison.OrdinalIgnoreCase)
                ? "landscape"
                : "portrait";

            return $@"<!DOCTYPE html>
<html lang=""{documentLanguage}"">
<head>
    <meta charset=""UTF-8""/>
    <title>{System.Net.WebUtility.HtmlEncode(documentTitle)}</title>
    <style>
        @page {{
            size: {orientation};
            margin: {marginTop}mm {marginRight}mm {marginBottom}mm {marginLeft}mm;
        }}
        img {{
            max-width: 100%;
            height: auto;
        }}
    </style>
</head>
<body style=""font-family: 'Liberation Serif', 'Times New Roman', Times, serif; font-size: 16px;"">
{htmlContent}
</body>
</html>";
        }

        /// <summary>
        /// Custom tag worker factory that overrides heading processing to avoid
        /// iText creating P structure elements inside H1-H6 headings.
        /// </summary>
        private class AccessibleTagWorkerFactory : DefaultTagWorkerFactory
        {
            public override ITagWorker GetCustomTagWorker(IElementNode tag, ProcessorContext context)
            {
                string name = tag.Name();
                if (name == "h1" || name == "h2" || name == "h3" ||
                    name == "h4" || name == "h5" || name == "h6")
                {
                    return new AccessibleHeadingTagWorker(tag, context);
                }
                if (name == "img")
                {
                    return new AccessibleImageTagWorker(tag, context);
                }
                return null;
            }
        }

        /// <summary>
        /// Processes heading tags as Paragraph elements (like PTagWorker) but sets
        /// the PDF structure role to H1-H6. This produces a single heading structure
        /// element without an intermediate P child.
        /// </summary>
        private class AccessibleHeadingTagWorker : PTagWorker
        {
            private readonly string headingRole;

            public AccessibleHeadingTagWorker(IElementNode element, ProcessorContext context)
                : base(element, context)
            {
                headingRole = element.Name().ToUpperInvariant();
            }

            public override IPropertyContainer GetElementResult()
            {
                var result = base.GetElementResult();
                if (result is iText.Layout.Element.Paragraph paragraph)
                {
                    paragraph.GetAccessibilityProperties().SetRole(headingRole);
                }
                return result;
            }
        }

        /// <summary>
        /// Processes img tags to ensure 508/PDF-UA compliance for inline images.
        /// Sets the Figure role and alternate description from the HTML alt attribute.
        /// Decorative images (alt="") are excluded from the structure tree.
        /// Images without an alt attribute receive a fallback description.
        /// </summary>
        private class AccessibleImageTagWorker : ImgTagWorker
        {
            private readonly IElementNode _element;

            public AccessibleImageTagWorker(IElementNode element, ProcessorContext context)
                : base(element, context)
            {
                _element = element;
            }

            public override IPropertyContainer GetElementResult()
            {
                var result = base.GetElementResult();

                if (result is iText.Layout.Element.Image image)
                {
                    string? altText = _element.GetAttribute("alt");
                    var accessibilityProperties = image.GetAccessibilityProperties();

                    if (altText != null && altText.Length == 0)
                    {
                        // Empty alt="" indicates a decorative image.
                        // For PDF/UA, decorative content should not appear in the
                        // structure tree. Setting the role to null marks it as an artifact.
                        accessibilityProperties.SetRole(null);
                    }
                    else if (!string.IsNullOrEmpty(altText))
                    {
                        // Meaningful image with alt text
                        accessibilityProperties.SetRole(StandardRoles.FIGURE);
                        accessibilityProperties.SetAlternateDescription(altText);
                    }
                    else
                    {
                        // No alt attribute — set Figure role with fallback description
                        // to maintain 508 compliance
                        accessibilityProperties.SetRole(StandardRoles.FIGURE);
                        accessibilityProperties.SetAlternateDescription("Image");
                    }
                }

                return result;
            }
        }
    }
}

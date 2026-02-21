using iText.Html2pdf;
using iText.Html2pdf.Attach;
using iText.Html2pdf.Attach.Impl;
using iText.Html2pdf.Attach.Impl.Tags;
using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using iText.Layout;
using iText.Layout.Font;
using iText.StyledXmlParser.Node;
using Microsoft.Extensions.Logging;
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

        public byte[] GenerateAccessiblePdfFromHtml(string htmlContent, string documentTitle, string documentLanguage = "en-US")
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

                var converterProperties = new ConverterProperties();
                var fontProvider = new FontProvider();
                AddBundledFonts(fontProvider);
                foreach (var dir in GetSystemFontDirectories())
                {
                    _logger.LogInformation("Adding system font directory: {Dir}", dir);
                    fontProvider.AddDirectory(dir);
                }
                converterProperties.SetFontProvider(fontProvider);
                converterProperties.SetTagWorkerFactory(new AccessibleTagWorkerFactory());
                converterProperties.SetOutlineHandler(OutlineHandler.CreateStandardHandler());

                var wrappedHtml = WrapInHtmlDocument(htmlContent, documentTitle, documentLanguage);

                HtmlConverter.ConvertToPdf(wrappedHtml, pdfDocument, converterProperties);
            }

            return memoryStream.ToArray();
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
            byte[] existingXmp = pdfDocument.GetXmpMetadata();
            XMPMeta xmpMeta = existingXmp != null
                ? XMPMetaFactory.ParseFromBuffer(existingXmp)
                : XMPMetaFactory.Create();

            const string pdfUaIdSchema = "http://www.aiim.org/pdfua/ns/id/";
            const string pdfUaIdPrefix = "pdfuaid";
            XMPMetaFactory.GetSchemaRegistry().RegisterNamespace(pdfUaIdSchema, pdfUaIdPrefix);

            xmpMeta.SetPropertyInteger(pdfUaIdSchema, "part", 1);

            const string dcSchema = "http://purl.org/dc/elements/1.1/";
            xmpMeta.SetLocalizedText(dcSchema, "title", "x-default", "x-default", documentTitle);

            pdfDocument.SetXmpMetadata(xmpMeta);
        }

        private static string WrapInHtmlDocument(string htmlContent, string documentTitle, string documentLanguage)
        {
            if (htmlContent.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || htmlContent.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return htmlContent;
            }

            return $@"<!DOCTYPE html>
<html lang=""{documentLanguage}"">
<head>
    <meta charset=""UTF-8""/>
    <title>{System.Net.WebUtility.HtmlEncode(documentTitle)}</title>
    <style>
        @page {{
            margin: 10mm;
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
    }
}

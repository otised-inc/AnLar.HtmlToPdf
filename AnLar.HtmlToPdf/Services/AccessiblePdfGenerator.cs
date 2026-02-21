using iText.Html2pdf;
using iText.Html2pdf.Attach;
using iText.Html2pdf.Attach.Impl;
using iText.Html2pdf.Attach.Impl.Tags;
using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using iText.Layout;
using iText.Layout.Font;
using iText.StyledXmlParser.Node;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AnLar.HtmlToPdf.Services
{
    public class AccessiblePdfGenerator
    {
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
                fontProvider.AddStandardPdfFonts();
                foreach (var dir in GetSystemFontDirectories())
                {
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
<body style=""font-family: 'Times New Roman', Times, serif; font-size: 16px;"">
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

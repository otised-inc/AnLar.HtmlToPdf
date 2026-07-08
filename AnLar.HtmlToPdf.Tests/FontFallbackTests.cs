using AnLar.HtmlToPdf.Services;
using iText.Html2pdf;
using iText.IO.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AnLar.HtmlToPdf.Tests
{
    /// <summary>
    /// Regression tests for font fallback after Pinyon Script joined the bundled
    /// fonts: generic-family fallback (monospace/serif/sans-serif/unknown names)
    /// and font-weight:500 text must never resolve to the cursive script face,
    /// while name-based lookup of 'Pinyon Script' must keep working.
    /// </summary>
    public class FontFallbackTests
    {
        private const string Probe = "SigProbe42";

        private readonly AccessiblePdfGenerator _generator;

        public FontFallbackTests()
        {
            _generator = new AccessiblePdfGenerator(new NullLogger<AccessiblePdfGenerator>());
        }

        // Criterion (a): generic families and named families with no bundled match
        // fall back to Liberation Serif, never Pinyon Script.
        [Theory]
        [InlineData("font-family: monospace;")]
        [InlineData("font-family: serif;")]
        [InlineData("font-family: sans-serif;")]
        [InlineData("font-family: Courier;")]
        [InlineData("font-family: 'Courier New', Courier, monospace;")]
        public void UnmatchedOrGenericFamily_FallsBackToLiberationSerif(string style)
        {
            var pdf = _generator.GenerateAccessiblePdfFromHtml(
                $"<p style=\"{style}\">{Probe}</p>", "Fallback Test");

            var font = FontUsedFor(pdf, Probe);
            Assert.Contains("LiberationSerif", font);
        }

        // Criterion (b): font-weight 500 must resolve a Liberation Serif face
        // (nearest weight), never Pinyon — including the stack observed in
        // production (Liberation named first, monospace generic later).
        [Theory]
        [InlineData("font-family: 'Liberation Serif'; font-weight: 500;")]
        [InlineData("font-family: 'Liberation Serif', Consolas, monospace; font-weight: 500;")]
        public void Weight500_ResolvesLiberationSerif_NeverPinyon(string style)
        {
            var pdf = _generator.GenerateAccessiblePdfFromHtml(
                $"<p style=\"{style}\">{Probe}</p>", "Weight 500 Test");

            var font = FontUsedFor(pdf, Probe);
            Assert.Contains("LiberationSerif", font);
        }

        // Weights around 500 already resolved correctly; pin that behavior.
        [Theory]
        [InlineData(400)]
        [InlineData(500)]
        [InlineData(600)]
        [InlineData(700)]
        public void AllWeights_OnLiberationStack_ResolveLiberationSerif(int weight)
        {
            var pdf = _generator.GenerateAccessiblePdfFromHtml(
                $"<p style=\"font-family: 'Liberation Serif'; font-weight: {weight};\">{Probe}</p>",
                "Weight Matrix Test");

            var font = FontUsedFor(pdf, Probe);
            Assert.Contains("LiberationSerif", font);
        }

        // The production probe ran with accessible:false — cover the fast path too.
        [Fact]
        public void UnmatchedFamily_FastMode_FallsBackToLiberationSerif()
        {
            var pdf = _generator.GenerateAccessiblePdfFromHtml(
                $"<p style=\"font-family: monospace; font-weight: 500;\">{Probe}</p>",
                "Fast Mode Fallback Test", accessible: false);

            var font = FontUsedFor(pdf, Probe);
            Assert.Contains("LiberationSerif", font);
        }

        // Criterion (c): name-based lookup of the bundled cursive font still works.
        [Fact]
        public void PinyonScript_RequestedByName_IsUsed()
        {
            var pdf = _generator.GenerateAccessiblePdfFromHtml(
                $"<p style=\"font-family: 'Pinyon Script', cursive;\">{Probe}</p>",
                "Pinyon By Name Test");

            var font = FontUsedFor(pdf, Probe);
            Assert.Contains("PinyonScript", font);
        }

        // Criterion (c): an @font-face whose src cannot be resolved must keep
        // being ignored gracefully, without disturbing name-based lookup.
        [Fact]
        public void PinyonScript_RequestedByName_WithUnresolvableFontFace_IsStillUsed()
        {
            var html =
                "<style>@font-face { font-family: 'Missing Font'; src: url('missing-font.woff2'); }</style>" +
                $"<p style=\"font-family: 'Pinyon Script', cursive;\">{Probe}</p>";

            var pdf = _generator.GenerateAccessiblePdfFromHtml(html, "Pinyon FontFace Test");

            var font = FontUsedFor(pdf, Probe);
            Assert.Contains("PinyonScript", font);
        }

        // Reproduces the production failure deterministically: on Linux,
        // Directory.EnumerateFiles order is arbitrary, so Pinyon can register
        // BEFORE the Liberation faces — and iText's FontSelector breaks score
        // ties by registration order. Selection must not depend on it.
        [Theory]
        [InlineData("font-family: monospace;")]
        [InlineData("font-family: serif;")]
        [InlineData("font-family: sans-serif;")]
        [InlineData("font-family: 'Courier New', Courier, monospace;")]
        [InlineData("font-family: 'Liberation Serif', Consolas, monospace; font-weight: 500;")]
        public void FontSelection_IsIndependentOfRegistrationOrder(string style)
        {
            var provider = new FallbackSafeFontProvider("Liberation Serif");
            foreach (var path in FontPathsAdversarialOrder())
                provider.AddFont(FontProgramFactory.CreateFont(path));

            using var ms = new MemoryStream();
            var props = new ConverterProperties().SetFontProvider(provider);
            HtmlConverter.ConvertToPdf($"<p style=\"{style}\">{Probe}</p>", ms, props);

            var font = FontUsedFor(ms.ToArray(), Probe);
            Assert.Contains("LiberationSerif", font);
        }

        [Fact]
        public void FontSelection_PinyonFirstRegistration_NameLookupStillWorks()
        {
            var provider = new FallbackSafeFontProvider("Liberation Serif");
            foreach (var path in FontPathsAdversarialOrder())
                provider.AddFont(FontProgramFactory.CreateFont(path));

            using var ms = new MemoryStream();
            var props = new ConverterProperties().SetFontProvider(provider);
            HtmlConverter.ConvertToPdf(
                $"<p style=\"font-family: 'Pinyon Script';\">{Probe}</p>", ms, props);

            var font = FontUsedFor(ms.ToArray(), Probe);
            Assert.Contains("PinyonScript", font);
        }

        // Order chosen so iText's merge sort compares Bold vs Italic (a score tie
        // at font-weight:500, which walks the family stack to the "monospace"
        // entry and mutates the shared FontCharacteristics) before it places
        // Regular vs Pinyon — the worst case observed on Linux, where directory
        // enumeration order is arbitrary.
        private static IReadOnlyList<string> FontPathsAdversarialOrder()
        {
            var dir = ResolveFontsDirectory();
            string P(string name) => Path.Combine(dir, name);
            return new[]
            {
                P("LiberationSerif-Bold.ttf"),
                P("LiberationSerif-Italic.ttf"),
                P("PinyonScript-Regular.ttf"),
                P("LiberationSerif-BoldItalic.ttf"),
                P("LiberationSerif-Regular.ttf"),
            };
        }

        private static string ResolveFontsDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Fonts"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AnLar.HtmlToPdf", "Fonts"),
            };
            foreach (var dir in candidates)
            {
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.ttf").Any())
                    return Path.GetFullPath(dir);
            }
            throw new DirectoryNotFoundException("Bundled Fonts directory not found for tests.");
        }

        /// <summary>
        /// Extracts the PostScript name of the font that rendered the text chunk
        /// containing <paramref name="needle"/> (e.g. "QXACAI+PinyonScript-Regular").
        /// </summary>
        private static string FontUsedFor(byte[] pdfBytes, string needle)
        {
            var chunks = new List<(string Text, string FontName)>();
            using (var ms = new MemoryStream(pdfBytes))
            using (var reader = new PdfReader(ms))
            using (var pdfDoc = new PdfDocument(reader))
            {
                for (int p = 1; p <= pdfDoc.GetNumberOfPages(); p++)
                    new PdfCanvasProcessor(new FontCaptureListener(chunks)).ProcessPageContent(pdfDoc.GetPage(p));
            }

            foreach (var chunk in chunks)
            {
                if (chunk.Text.Contains(needle, StringComparison.Ordinal))
                    return chunk.FontName;
            }

            var dump = string.Join(" | ", chunks.Select(c => $"'{c.Text}' ({c.FontName})"));
            Assert.Fail($"No text chunk containing '{needle}' found. Chunks: {dump}");
            return string.Empty;
        }

        private sealed class FontCaptureListener : IEventListener
        {
            private readonly List<(string Text, string FontName)> _chunks;

            public FontCaptureListener(List<(string Text, string FontName)> chunks) => _chunks = chunks;

            public void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_TEXT && data is TextRenderInfo tri)
                {
                    var name = tri.GetFont()?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "<unknown>";
                    _chunks.Add((tri.GetText(), name));
                }
            }

            public ICollection<EventType>? GetSupportedEvents() => null;
        }
    }
}

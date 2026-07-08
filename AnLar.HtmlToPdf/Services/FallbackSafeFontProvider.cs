using iText.Layout.Font;

namespace AnLar.HtmlToPdf.Services
{
    /// <summary>
    /// A <see cref="FontProvider"/> whose fallback selection never resolves to a
    /// bundled decorative/script face (e.g. Pinyon Script) unless that face was
    /// requested by name.
    /// </summary>
    /// <remarks>
    /// iText's <see cref="FontSelector"/> comparator shares one mutable
    /// <see cref="FontCharacteristics"/> across every entry of the font-family
    /// stack; encountering a "monospace" entry mid-sort sets its monospace flag
    /// permanently, which suppresses family-name matching (including the default
    /// font family) for all comparisons after that point. Fonts that then tie on
    /// pure style scoring — Liberation Serif Regular vs Pinyon Script, both
    /// weight 400 upright — are ordered only by registration order, which follows
    /// filesystem enumeration order and is arbitrary on Linux. This provider
    /// breaks such score ties in favor of faces belonging to the default font
    /// family, making selection deterministic and registration-order-independent.
    /// </remarks>
    public sealed class FallbackSafeFontProvider : FontProvider
    {
        public FallbackSafeFontProvider(string defaultFontFamily)
            : base(defaultFontFamily)
        {
        }

        protected override FontSelector CreateFontSelector(
            ICollection<FontInfo> fonts, IList<string> fontFamilies, FontCharacteristics fc)
        {
            // Mirrors the base implementation: the default font family is
            // appended as the last entry of the requested family list.
            var families = new List<string>(fontFamilies) { GetDefaultFontFamily() };
            return new DefaultFamilyTieBreakingFontSelector(fonts, families, fc);
        }

        private sealed class DefaultFamilyTieBreakingFontSelector : FontSelector
        {
            public DefaultFamilyTieBreakingFontSelector(
                ICollection<FontInfo> fonts, IList<string> fontFamilies, FontCharacteristics fc)
                : base(fonts, fontFamilies, fc)
            {
            }

            protected override IComparer<FontInfo> GetComparator(
                IList<string> fontFamilies, FontCharacteristics fc)
            {
                var baseComparator = base.GetComparator(fontFamilies, fc);
                // Invoked from the base constructor — instance state is not yet
                // initialized here, so only the arguments may be used. The default
                // font family is the last entry (appended in CreateFontSelector).
                var defaultFamily = fontFamilies.Count > 0
                    ? fontFamilies[fontFamilies.Count - 1]
                    : string.Empty;

                return Comparer<FontInfo>.Create((f1, f2) =>
                {
                    int result = baseComparator.Compare(f1, f2);
                    if (result != 0 || defaultFamily.Length == 0)
                        return result;
                    bool f1IsDefault = IsFamily(f1, defaultFamily);
                    bool f2IsDefault = IsFamily(f2, defaultFamily);
                    // Fonts are sorted best-first: on a score tie, a face of the
                    // default family outranks any other face.
                    return f1IsDefault == f2IsDefault ? 0 : (f1IsDefault ? -1 : 1);
                });
            }

            private static bool IsFamily(FontInfo fontInfo, string family)
            {
                // Like iText's own matching, an alias (set for @font-face fonts)
                // takes precedence over the font program's family name.
                var alias = fontInfo.GetAlias();
                if (alias != null)
                    return string.Equals(alias, family, StringComparison.OrdinalIgnoreCase);
                return string.Equals(
                    fontInfo.GetDescriptor()?.GetFamilyNameLowerCase(), family,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

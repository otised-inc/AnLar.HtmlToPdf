namespace AnLar.HtmlToPdf.DTOs
{
    public class PdfRequest
    {
        public string HtmlContent { get; set; } = string.Empty;
        public string? DocumentTitle { get; set; }
        public string? DocumentLanguage { get; set; }

        /// <summary>
        /// Page orientation. Accepted values: "portrait" (default) or "landscape".
        /// </summary>
        public string? PageOrientation { get; set; }

        /// <summary>
        /// Top page margin in millimeters. Defaults to 10.
        /// </summary>
        public float? MarginTop { get; set; }

        /// <summary>
        /// Right page margin in millimeters. Defaults to 10.
        /// </summary>
        public float? MarginRight { get; set; }

        /// <summary>
        /// Bottom page margin in millimeters. Defaults to 10.
        /// </summary>
        public float? MarginBottom { get; set; }

        /// <summary>
        /// Left page margin in millimeters. Defaults to 10.
        /// </summary>
        public float? MarginLeft { get; set; }

        /// <summary>
        /// When true, adds "Page X of Y" centered at the bottom of each page. Defaults to false.
        /// </summary>
        public bool? ShowPageNumbers { get; set; }

        /// <summary>
        /// Optional watermark text to render diagonally across each page (e.g. "DRAFT", "CONFIDENTIAL").
        /// When null or empty, no watermark is added.
        /// </summary>
        public string? Watermark { get; set; }

        /// <summary>
        /// Resolution in dots per inch for the /pdf/images endpoint. Defaults to 300.
        /// Higher values produce larger, sharper images.
        /// </summary>
        public int? Dpi { get; set; }
    }
}

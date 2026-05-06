namespace AnLar.HtmlToPdf.DTOs
{
    /// <summary>
    /// One page emitted by the streaming /pdf/images/stream endpoint.
    /// Serialized as a single JSON object per NDJSON line.
    /// </summary>
    public sealed record PageImage(int Page, int TotalPages, string Base64);
}

using System.Text.Encodings.Web;
using System.Text.Json;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// JSON serialization aligned with human-readable Unicode in files and callbacks (like Python <c>ensure_ascii=False</c>).
/// </summary>
public static class TranscriptionJsonSerializerOptions
{
    /// <summary>Indented output for artifacts (chunk JSON, combined JSON, cache manifest).</summary>
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Compact output for fallbacks and webhook bodies.</summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

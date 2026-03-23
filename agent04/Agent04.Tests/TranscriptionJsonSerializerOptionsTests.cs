using System.Text.Json;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class TranscriptionJsonSerializerOptionsTests
{
    [Fact]
    public void Indented_serializes_spanish_and_cyrillic_without_ascii_unicode_escapes()
    {
        var payload = new Dictionary<string, object?>
        {
            ["text"] = "¿Qué tal? Это тест."
        };

        var defaultJson = JsonSerializer.Serialize(payload);
        Assert.Contains("\\u", defaultJson, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(payload, TranscriptionJsonSerializerOptions.Indented);
        Assert.Contains("¿Qué tal? Это тест.", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00BF", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u042D", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_matches_relaxed_encoder_for_webhook_style_payload()
    {
        var payload = new Dictionary<string, object?> { ["x"] = "café" };
        var json = JsonSerializer.Serialize(payload, TranscriptionJsonSerializerOptions.Compact);
        Assert.Contains("café", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00E9", json, StringComparison.Ordinal);
    }
}

namespace Agent04.Features.Transcription.Application;

public sealed class SubmitJobRequest
{
    public string? ConfigPath { get; set; }
    public string? InputFilePath { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    /// <summary>Optional. If set, HTTP POST is sent to this URL when the job completes or fails (body: job status JSON).</summary>
    public string? CallbackUrl { get; set; }
}

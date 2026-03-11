namespace Agent04.Features.Transcription.Application;

public sealed class SubmitJobRequest
{
    public string? ConfigPath { get; set; }
    public string? InputFilePath { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
}

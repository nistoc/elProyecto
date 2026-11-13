namespace Agent04.Features.Transcription.Application;

/// <summary>
/// The server refused the request with time marks three times in a row (HTTP 5xx).
/// Thrown only when the caller asked for it with <see cref="TranscriptionClientOptions.StopAtServerError"/>:
/// without that word the client goes on to requests without <c>response_format</c>, and a diarize model
/// then answers plain text without time marks.
/// </summary>
public sealed class TranscriptionServerErrorException : Exception
{
    public TranscriptionServerErrorException(int statusCode, int attempts, string detail, Exception? inner = null)
        : base($"Server error {statusCode} on {attempts} attempts: {detail}", inner)
    {
        StatusCode = statusCode;
        Attempts = attempts;
        Detail = detail;
    }

    /// <summary>HTTP status of the last attempt (500, 502, …).</summary>
    public int StatusCode { get; }

    /// <summary>How many attempts were made before giving up.</summary>
    public int Attempts { get; }

    /// <summary>Short text of the server's answer, for logs and for the chunk's record.</summary>
    public string Detail { get; }
}

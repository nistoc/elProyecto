namespace Agent04.Features.Transcription.Application;

/// <summary>
/// The server refused the request because of the account, not because of the audio: no credits left, the
/// key is not accepted, the account is closed. No other request of the job can pass until a person
/// changes that, so the request is not tried again, in no other form and with no other model, and the
/// whole job stops (<see cref="TranscriptionWorkflowAbort.ShouldAbortWholeJob"/>). The message is written
/// for the person who reads the job's error in the UI.
/// </summary>
public sealed class TranscriptionAccountRefusedException : Exception
{
    public TranscriptionAccountRefusedException(string reason, string detail, Exception? inner = null)
        : base(Describe(reason), inner)
    {
        Reason = reason;
        Detail = detail;
    }

    /// <summary>The server's word for the refusal: <c>insufficient_quota</c>, <c>invalid_api_key</c>, <c>401</c>…</summary>
    public string Reason { get; }

    /// <summary>Short text of the server's answer, for logs.</summary>
    public string Detail { get; }

    private static string Describe(string reason) => reason switch
    {
        "insufficient_quota" or "billing_not_active" =>
            $"OpenAI refused the request: the account has no credits left ({reason}). Add credits to the account and start the job again.",
        "invalid_api_key" or "401" =>
            $"OpenAI refused the API key ({reason}). Check OPENAI_API_KEY of agent04 and start the job again.",
        _ =>
            $"OpenAI refused access for this account ({reason}). Check the account and the API key, then start the job again.",
    };
}

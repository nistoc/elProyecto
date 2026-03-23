using System.Net.Http;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Decides whether an exception should stop the entire job (cancel all in-flight chunks)
/// vs only failing the current chunk. Global abort: auth, billing/quota — no point retrying without config change.
/// </summary>
public static class TranscriptionWorkflowAbort
{
    /// <summary>Returns true if the whole transcription job should be cancelled (all workers).</summary>
    public static bool ShouldAbortWholeJob(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is HttpRequestException hre)
            {
                if (hre.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    hre.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return true;
            }

            var msg = e.Message;
            if (ContainsGlobalToken(msg))
                return true;

            // OpenAI client wraps as InvalidOperationException with "401: {...}" in message
            if (msg.Length >= 4 && char.IsDigit(msg[0]) && msg[1] is >= '0' and <= '9' && msg[2] is >= '0' and <= '9')
            {
                if (msg.StartsWith("401:", StringComparison.Ordinal) || msg.StartsWith("403:", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsGlobalToken(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("billing_not_active", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("account_deactivated", StringComparison.OrdinalIgnoreCase);
    }
}

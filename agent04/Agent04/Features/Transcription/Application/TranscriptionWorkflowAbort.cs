using System.Net.Http;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Decides whether an exception should stop the entire job (cancel all in-flight chunks)
/// vs only failing the current chunk. Global abort: auth, billing/quota — no point retrying without config change.
/// </summary>
public static class TranscriptionWorkflowAbort
{
    /// <summary>Returns true if the whole transcription job should be cancelled (all workers).</summary>
    public static bool ShouldAbortWholeJob(Exception ex) => RefusalReason(ex) != null;

    /// <summary>
    /// The server's word for a refusal that concerns the account (<c>insufficient_quota</c>,
    /// <c>invalid_api_key</c>, <c>billing_not_active</c>, <c>account_deactivated</c>, else <c>401</c> or
    /// <c>403</c>), found in the exception or in what caused it; null when the failure is of another kind.
    /// </summary>
    public static string? RefusalReason(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is TranscriptionAccountRefusedException refused)
                return refused.Reason;

            var msg = e.Message;
            var token = GlobalToken(msg);
            if (token != null)
                return token;

            if (e is HttpRequestException hre)
            {
                if (hre.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return "401";
                if (hre.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return "403";
            }

            // OpenAI client wraps as InvalidOperationException with "401: {...}" in message
            if (msg.StartsWith("401:", StringComparison.Ordinal))
                return "401";
            if (msg.StartsWith("403:", StringComparison.Ordinal))
                return "403";
        }

        return null;
    }

    private static readonly string[] GlobalTokens =
        { "insufficient_quota", "invalid_api_key", "billing_not_active", "account_deactivated" };

    private static string? GlobalToken(string? msg) =>
        string.IsNullOrEmpty(msg)
            ? null
            : GlobalTokens.FirstOrDefault(token => msg.Contains(token, StringComparison.OrdinalIgnoreCase));
}

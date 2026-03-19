using System.Text.Json;

namespace XtractManager.Infrastructure;

/// <summary>
/// JSON options aligned with ASP.NET <see cref="Microsoft.AspNetCore.Mvc.JsonResult"/> defaults
/// so SSE payloads match <c>GET /api/jobs/{id}</c> (camelCase). Raw <c>JsonSerializer.Serialize(job)</c>
/// without options emits PascalCase and breaks the UI until the next HTTP fetch.
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);
}

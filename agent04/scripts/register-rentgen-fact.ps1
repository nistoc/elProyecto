# Register RENTGEN_IMPLEMENTATION.md as a fact in Knowledge Store (http://localhost:5173).
# Run when Knowledge Store is running. Requires: Invoke-RestMethod.

$baseUrl = $env:KNOWLEDGE_STORE_URL ?? "http://localhost:5173"
$docPath = Join-Path $PSScriptRoot "..\docs\RENTGEN_IMPLEMENTATION.md"
$content = Get-Content -Path $docPath -Raw -Encoding UTF8

$body = @{
    title   = "RENTGEN_IMPLEMENTATION.md"
    content = $content
    tags    = @("implementation-requirements", "rentgen", "virtual-model", "architecture")
} | ConvertTo-Json -Depth 10

$headers = @{
    "Content-Type" = "application/json"
    "X-Caller-Id"  = "agent04-setup"
}

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/facts" -Method Post -Body $body -Headers $headers -ErrorAction Stop
    Write-Host "Fact registered: $($response | ConvertTo-Json -Compress)"
} catch {
    Write-Error "Failed to register fact: $_"
    exit 1
}

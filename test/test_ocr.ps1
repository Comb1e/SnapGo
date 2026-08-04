# SnapGo OCR Pipeline Diagnostic Script
# Tests each layer independently to isolate failures
param(
    [string]$BaseUrl = "http://localhost:8000",
    [string]$TestImagePath = ""
)

$ErrorActionPreference = "Continue"
$Results = @()

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Pass($msg)  { Write-Host "  PASS: $msg" -ForegroundColor Green; $global:Results += "PASS: $msg" }
function Fail($msg)  { Write-Host "  FAIL: $msg" -ForegroundColor Red;   $global:Results += "FAIL: $msg" }
function Info($msg)  { Write-Host "  INFO: $msg" -ForegroundColor Gray }

# ============================================================
# Layer 1: Docker Container
# ============================================================
Write-Step "Layer 1: Docker Container Status"
$status = docker ps --filter name=snapgo-ocr --format "{{.Status}}" 2>&1
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($status)) {
    Fail "Container 'snapgo-ocr' not found or not running"
    Info "Run: cd docker && docker compose up -d"
    exit 1
}
if ($status -match "^Up") { Pass "Container running: $status" }
else { Fail "Container status: $status" }

# ============================================================
# Layer 2: Health Endpoint
# ============================================================
Write-Step "Layer 2: Health Endpoint"
try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health" -TimeoutSec 5 -ErrorAction Stop
    if ($health.errorCode -eq 0) { Pass "Health check OK: $($health.errorMsg)" }
    else { Fail "Health check returned errorCode=$($health.errorCode)" }
} catch {
    Fail "Health endpoint unreachable: $_"
    exit 1
}

# ============================================================
# Layer 3: Container Logs (check for VLM errors)
# ============================================================
Write-Step "Layer 3: Container Error Check"
$logs = docker logs snapgo-ocr --tail 20 2>&1
if ($logs -match "Connection error" -or $logs -match "vlm.*worker") {
    Fail "VLM worker connection error detected in container logs"
    Info "The pipeline cannot connect to the vLLM model server."
    Info "See: docker logs snapgo-ocr --tail 50"
} elseif ($logs -match "RuntimeError") {
    Fail "Runtime errors in container logs"
    Info "Last error lines:"
    $logs -split "`n" | Select-String "Error|error|Exception" | ForEach-Object { Info $_.Line }
} else {
    Pass "No obvious errors in recent container logs"
}

# ============================================================
# Layer 4: OCR Endpoint (layout-parsing)
# ============================================================
Write-Step "Layer 4: OCR Pipeline Test"

# Use provided test image or generate a minimal one
if ($TestImagePath -and (Test-Path $TestImagePath)) {
    Info "Using test image: $TestImagePath"
    $imgBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $TestImagePath))
    $base64 = [Convert]::ToBase64String($imgBytes)
} else {
    Info "No test image provided, generating minimal PNG..."
    # Generate a 200x50 white PNG (will produce empty OCR but validates the pipeline)
    $pngBytes = [byte[]]@(
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,  # PNG header
        0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,0x00,0x00,0x00,0xC8,0x00,0x00,0x00,0x32,0x08,0x02,0x00,0x00,0x00,0x9A,0x5A,0xB3,0x0E,  # IHDR 200x50 8bpp RGB
        0x00,0x00,0x00,0x01,0x73,0x52,0x47,0x42,0x00,0xAE,0xCE,0x1C,0xE9,  # sRGB
        0x00,0x00,0x00,0x20,0x49,0x44,0x41,0x54,0x78,0x9C,0xED,0xC1,0x31,0x01,0x00,0x00,0x00,0xC2,0xA0,0xF5,0x4F,0x6D,0x0C,0x20,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x30,0x03,0xF8,0x01,0x00,0x01,0x43,0xBD,0x60,  # IDAT (white pixels)
        0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82   # IEND
    )
    $base64 = [Convert]::ToBase64String($pngBytes)
}

$body = @{
    file = $base64
    fileType = 1
} | ConvertTo-Json -Compress

try {
    $result = Invoke-RestMethod -Uri "$BaseUrl/layout-parsing" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 60
    if ($result.errorCode -eq 0) {
        $count = if ($result.result.layoutParsingResults) { $result.result.layoutParsingResults.Count } else { 0 }
        Pass "OCR pipeline returned OK ($count layout result(s))"

        # Extract text if present
        $textFound = $false
        foreach ($lr in $result.result.layoutParsingResults) {
            if ($lr.markdown.text) {
                $textFound = $true
                $preview = if ($lr.markdown.text.Length -gt 100) { $lr.markdown.text.Substring(0, 100) + "..." } else { $lr.markdown.text }
                Info "Recognized text: $preview"
            }
        }
        if (-not $textFound) {
            Info "(No text in result — expected for blank test image)"
        }
    } else {
        Fail "OCR returned errorCode=$($result.errorCode): $($result.errorMsg)"
    }
} catch {
    $err = $_.Exception.Message
    if ($err -match "500") {
        Fail "OCR returned HTTP 500 — VLM worker connection failure"
        Info "The PaddleX pipeline needs a vLLM model server backend."
        Info "Current pipeline config points to: http://paddleocr-vlm-server:8080/v1"
        Info "This is a SEPARATE container that is not running."
    } else {
        Fail "OCR request failed: $err"
    }
}

# ============================================================
# Layer 5: Pipeline Config
# ============================================================
Write-Step "Layer 5: Pipeline Configuration"
try {
    $config = docker exec snapgo-ocr sh -c 'cat /home/paddleocr/pipeline_config_vllm.yaml' 2>&1
    if ($config -match "vllm-server") {
        Info "Pipeline uses vLLM backend (requires separate vLLM server container)"
        $config -split "`n" | Select-String "server_url|backend" | ForEach-Object { Info $_.Line.Trim() }
    }
} catch {
    Info "Could not read pipeline config"
}

# ============================================================
# Summary
# ============================================================
Write-Step "Summary"
$passCount = ($Results | Where-Object { $_ -match "^PASS:" }).Count
$failCount = ($Results | Where-Object { $_ -match "^FAIL:" }).Count
Write-Host "  Passed: $passCount  Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
foreach ($r in $Results) { Write-Host "  $r" }

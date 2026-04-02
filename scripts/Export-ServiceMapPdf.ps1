[CmdletBinding()]
param(
    [string]$InputMarkdown = "C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md",
    [string]$OutputPdf = "C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.pdf",
    [string]$OutputHtml = "C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.print.html"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ConvertFrom-Markdown -ErrorAction SilentlyContinue)) {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh -and $pwsh.Source -and $pwsh.Source -ne (Get-Process -Id $PID).Path) {
        & $pwsh.Source -ExecutionPolicy Bypass -File $PSCommandPath `
            -InputMarkdown $InputMarkdown `
            -OutputPdf $OutputPdf `
            -OutputHtml $OutputHtml
        exit $LASTEXITCODE
    }

    throw "ConvertFrom-Markdown is unavailable in this PowerShell host. Run this script with PowerShell 7 (pwsh)."
}

function Resolve-BrowserPath {
    $candidates = @(
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw "Could not find Edge or Chrome for PDF export."
}

function New-HtmlDocument {
    param(
        [string]$Title,
        [string]$BodyHtml
    )

    @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>$Title</title>
  <style>
    @page {
      size: Letter landscape;
      margin: 0.6in;
    }

    :root {
      --bg: #f6f3ed;
      --surface: #ffffff;
      --surface-alt: #f3f6f9;
      --text: #1d252c;
      --muted: #5a6670;
      --rule: #d5dfe6;
      --accent: #1f5b75;
      --accent-soft: #e7f1f6;
      --code-bg: #eef3f6;
    }

    * {
      box-sizing: border-box;
    }

    html, body {
      margin: 0;
      padding: 0;
      background: var(--bg);
      color: var(--text);
      font-family: "Segoe UI", "Aptos", Arial, sans-serif;
      font-size: 11px;
      line-height: 1.45;
    }

    body {
      padding: 0.25in;
    }

    .page {
      background: var(--surface);
      border: 1px solid var(--rule);
      border-radius: 18px;
      padding: 24px 28px;
      box-shadow: 0 8px 24px rgba(30, 42, 51, 0.06);
    }

    h1, h2, h3 {
      margin: 0;
      page-break-after: avoid;
      break-after: avoid-page;
    }

    h1 {
      font-size: 24px;
      line-height: 1.2;
      margin-bottom: 8px;
      color: #123243;
    }

    h2 {
      margin-top: 22px;
      padding-top: 10px;
      border-top: 2px solid var(--accent-soft);
      font-size: 16px;
      color: var(--accent);
    }

    h3 {
      margin-top: 18px;
      font-size: 13px;
      color: #21485b;
    }

    p, ul, ol {
      margin: 8px 0 0 0;
    }

    ul, ol {
      padding-left: 18px;
    }

    li {
      margin: 4px 0;
    }

    code {
      font-family: "Cascadia Mono", "Consolas", monospace;
      background: var(--code-bg);
      border-radius: 4px;
      padding: 1px 4px;
      font-size: 0.95em;
    }

    hr {
      border: 0;
      border-top: 1px solid var(--rule);
      margin: 18px 0;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 10px;
      table-layout: fixed;
      page-break-inside: avoid;
      break-inside: avoid;
      font-size: 10px;
    }

    thead {
      display: table-header-group;
    }

    tr {
      page-break-inside: avoid;
      break-inside: avoid;
    }

    th, td {
      border: 1px solid var(--rule);
      padding: 7px 8px;
      vertical-align: top;
      text-align: left;
      overflow-wrap: anywhere;
      word-break: break-word;
    }

    th {
      background: var(--accent-soft);
      color: #17384a;
      font-weight: 700;
    }

    tbody tr:nth-child(even) td {
      background: var(--surface-alt);
    }

    blockquote {
      margin: 12px 0 0 0;
      padding: 10px 14px;
      border-left: 4px solid var(--accent);
      background: #f4f9fb;
      color: #30444f;
    }

    .subtitle {
      color: var(--muted);
      margin-top: 4px;
      margin-bottom: 16px;
      font-size: 12px;
    }

    .callout {
      margin-top: 16px;
      padding: 12px 14px;
      border-radius: 12px;
      background: linear-gradient(135deg, #eef6fa, #f8fbfd);
      border: 1px solid #d6e7ef;
    }

    .callout strong {
      color: #123243;
    }
  </style>
</head>
<body>
  <main class="page">
    <div class="subtitle">PixelVault coordination handoff for splitting responsibilities out of MainWindow</div>
    <div class="callout">
      <strong>How to use this PDF:</strong> keep Cursor on UI-host and MainWindow surface work, keep Codex on new services/models/tests, and let only one side own the final integration pass in <code>PixelVault.Native.cs</code> or <code>ImportWorkflow.cs</code>.
    </div>
    $BodyHtml
  </main>
</body>
</html>
"@
}

$browserPath = Resolve-BrowserPath
$inputPath = [System.IO.Path]::GetFullPath($InputMarkdown)
$pdfPath = [System.IO.Path]::GetFullPath($OutputPdf)
$htmlPath = [System.IO.Path]::GetFullPath($OutputHtml)

if (-not (Test-Path $inputPath)) {
    throw "Input markdown not found: $inputPath"
}

$markdown = ConvertFrom-Markdown -Path $inputPath
$htmlBody = $markdown.Html
$fullHtml = New-HtmlDocument -Title "Service Ownership And Parallel Work Map" -BodyHtml $htmlBody

$htmlDirectory = Split-Path -Parent $htmlPath
$pdfDirectory = Split-Path -Parent $pdfPath
if (-not (Test-Path $htmlDirectory)) { New-Item -ItemType Directory -Path $htmlDirectory | Out-Null }
if (-not (Test-Path $pdfDirectory)) { New-Item -ItemType Directory -Path $pdfDirectory | Out-Null }

[System.IO.File]::WriteAllText($htmlPath, $fullHtml, [System.Text.Encoding]::UTF8)

$htmlUri = [System.Uri]::new($htmlPath).AbsoluteUri

& $browserPath `
    "--headless=new" `
    "--disable-gpu" `
    "--allow-file-access-from-files" `
    "--print-to-pdf-no-header" `
    "--print-to-pdf=$pdfPath" `
    $htmlUri | Out-Null

if (-not (Test-Path $pdfPath)) {
    throw "PDF export failed: $pdfPath was not created."
}

Write-Host "HTML: $htmlPath"
Write-Host "PDF:  $pdfPath"

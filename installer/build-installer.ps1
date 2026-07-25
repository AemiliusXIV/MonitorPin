# Publishes MonitorPin as a self-contained single exe, then compiles the
# installer with Inno Setup. Produces installer\output\MonitorPin-Setup-*.exe.

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent $here
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { throw "dotnet not found" }
}

Write-Host "Publishing self-contained build..."
& $dotnet publish "$root\MonitorPin.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup not found. Install it from https://jrsoftware.org/isdl.php and re-run."
    exit 1
}

Write-Host "Compiling installer with $iscc ..."
& $iscc "$here\MonitorPin.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Write-Host "Done. See installer\output\"

<#
  LiveDiag build script (ASCII only on purpose: avoids codepage issues)
  Requires only the .NET Framework compiler that ships with Windows.
  Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 [-Test] [-Estats] [-Run]
#>
param(
    [switch]$Run,
    [switch]$Test,
    [switch]$Estats
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found' }

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$files = @(Get-ChildItem -Path $src -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) { throw 'no .cs files under src' }

$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')
$exe = Join-Path $dist 'LiveDiag.exe'
$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/langversion:5',
    '/codepage:65001',
    '/warn:4',
    ('/out:' + $exe)
)
foreach ($r in $refs) { $cscArgs += ('/r:' + $r) }

Write-Host ('compiling ' + $files.Count + ' files ...') -ForegroundColor Cyan
& $csc $cscArgs $files
if ($LASTEXITCODE -ne 0) { throw ('compile failed, exit=' + $LASTEXITCODE) }

$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host ('OK -> ' + $exe + '  ' + $size + ' KB') -ForegroundColor Green

if ($Estats) {
    $f = Join-Path $dist 'estats.txt'
    if (Test-Path $f) { Remove-Item $f -Force }
    $p = Start-Process -FilePath $exe -ArgumentList '--estats' -PassThru
    $p.WaitForExit(30000) | Out-Null
    if (Test-Path $f) { Get-Content $f -Encoding UTF8 } else { Write-Host 'no estats.txt' -ForegroundColor Yellow }
}

if ($Test) {
    $f = Join-Path $dist 'selftest.txt'
    if (Test-Path $f) { Remove-Item $f -Force }
    $p = Start-Process -FilePath $exe -ArgumentList '--selftest','8' -PassThru
    $p.WaitForExit(60000) | Out-Null
    if (Test-Path $f) { Get-Content $f -Encoding UTF8 } else { Write-Host 'no selftest.txt' -ForegroundColor Yellow }
}

if ($Run) { Start-Process $exe }

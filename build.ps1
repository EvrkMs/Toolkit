param(
    # Куда положить Toolkit.exe и tkguard.exe. По умолчанию рядом со скриптом;
    # StartHidden.vbs собирает в %TEMP%, чтобы общая папка оставалась только для чтения.
    [string]$Out = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

# Встроенный компилятор Windows (C# 5): SDK не нужен. Ставится вместе с .NET Framework 4.x.
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "csc.exe not found: $csc"
}

$srcDir = Join-Path $PSScriptRoot "src"
if (-not (Test-Path $srcDir)) {
    throw "source directory missing: $srcDir"
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null

$guardDir = Join-Path $srcDir "Guard"

# 1. tkguard.exe первым: отдельный консольный exe (подмена taskkill для GameFix), см. src\Guard.
#    Он маленький и собирается быстро, а Toolkit.exe копирует его при старте, поэтому он должен быть готов раньше.
$guardSource = Join-Path $guardDir "TaskkillGuard.cs"
$guardExe = Join-Path $Out "tkguard.exe"
& $csc "/nologo" "/target:exe" "/platform:x64" "/optimize+" "/out:$guardExe" `
    "/reference:System.dll" "/reference:System.Core.dll" "/reference:System.Management.dll" $guardSource
if ($LASTEXITCODE -ne 0) {
    throw "guard build failed with exit code $LASTEXITCODE"
}

Write-Output "Built: $guardExe"

# 2. Toolkit.exe: все .cs из src, кроме src\Guard.
$sources = @(Get-ChildItem -Path $srcDir -Recurse -Filter *.cs |
    Where-Object { -not $_.FullName.StartsWith($guardDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    throw "no .cs files under $srcDir"
}

$exe = Join-Path $Out "Toolkit.exe"

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Runtime.Serialization.dll",
    "System.Xml.dll"
)

# /platform:x64 обязателен: ShortcutsVerb читает память explorer.exe,
# разрядность процессов должна совпадать.
# /target:winexe - оконная подсистема: автозагрузка не мигает чёрным окном.
# Вывод в терминал сохраняется через ConsoleBridge.AttachToParent().
$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/out:$exe"
)
$cscArgs += ($references | ForEach-Object { "/reference:$_" })
$cscArgs += $sources

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "build failed with exit code $LASTEXITCODE"
}

Write-Output "Built: $exe ($($sources.Count) sources)"

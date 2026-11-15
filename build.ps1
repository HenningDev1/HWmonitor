# Builds HWmonitor.exe (self-contained single file) and HWmonitor-Setup.exe into .\Release
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'Release'
$tmp = Join-Path $root 'artifacts'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out, $tmp | Out-Null

Write-Host 'Publishing HWmonitor'
dotnet publish "$root\src\HWmonitor\HWmonitor.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:GenerateDocumentationFile=false -o "$tmp\app"
if ($LASTEXITCODE) { throw 'publish failed' }

Write-Host 'Building setup stub'
dotnet build "$root\src\HWmonitor.Setup\HWmonitor.Setup.csproj" -c Release -p:DebugType=none -o "$tmp\setup"
if ($LASTEXITCODE) { throw 'setup build failed' }

Write-Host 'Packing installer'
$app = [IO.File]::ReadAllBytes("$tmp\app\HWmonitor.exe")
$stub = [IO.File]::ReadAllBytes("$tmp\setup\HWmonitor-Setup.exe")
$fs = [IO.File]::Create("$out\HWmonitor-Setup.exe")
$fs.Write($stub, 0, $stub.Length)
$fs.Write($app, 0, $app.Length)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([Int64]$app.Length)
$bw.Write([Int64]0x48574D4F4E495452)   # magic "HWMONITR"
$bw.Close()
Copy-Item "$tmp\app\HWmonitor.exe" "$out\HWmonitor.exe" -Force
Remove-Item $tmp -Recurse -Force

Get-ChildItem $out | ForEach-Object { '{0,-24} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }

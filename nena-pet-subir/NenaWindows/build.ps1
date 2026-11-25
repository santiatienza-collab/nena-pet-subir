# Compila Nena Pet (nativa, C# + WPF) con el compilador que ya trae Windows. Uso: powershell -File build.ps1 [-Out dist]
param([string]$Out = "dist", [string]$Define = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$fw\csc.exe"
$refs = @("$fw\WPF\PresentationFramework.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll",
          "$fw\System.Xaml.dll", "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
          "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll") | ForEach-Object { "-r:$_" }

New-Item -ItemType Directory -Force -Path "$root\$Out" | Out-Null
$icon = "$root\assets\icon.ico"
$iconArg = @(if (Test-Path $icon) { "-win32icon:$icon" })

$res = @(Get-ChildItem "$root\assets\frames3\*.png" | ForEach-Object { "-resource:$($_.FullName),f.$($_.Name)" })
Write-Host "Compilando NenaPet.exe ..."
$srcs = Get-ChildItem "$root\src\*.cs" | Where-Object { $_.Name -notlike "Setup*" -and $_.Name -notlike "Test*" } | ForEach-Object { $_.FullName }
$defArg = @(if ($Define) { "-define:$Define" })
& $csc -nologo @defArg -target:winexe -out:"$root\$Out\NenaPet.exe" -optimize+ @iconArg @res @refs @srcs
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion de NenaPet.exe" }
Write-Host "OK -> $root\$Out\NenaPet.exe"

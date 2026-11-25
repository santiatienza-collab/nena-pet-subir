# Compila Nena Pet y genera el instalador NenaPetSetup.exe (todo nativo, con el compilador de Windows).
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File build-installer.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$fw\csc.exe"

# 1) la aplicación
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\build.ps1" -Out dist
if ($LASTEXITCODE -ne 0) { throw "fallo build.ps1" }

# 2) contenido a instalar
$stage = "$root\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$stage\sounds" | Out-Null
Copy-Item "$root\dist\NenaPet.exe" "$stage\NenaPet.exe"
Copy-Item "$root\assets\sounds\*" "$stage\sounds\"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = "$root\payload.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)

# 3) el instalador (lleva el zip dentro como recurso)
$refs = @("$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
          "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll") | ForEach-Object { "-r:$_" }
$icon = "$root\assets\icon.ico"
$iconArg = @(if (Test-Path $icon) { "-win32icon:$icon" })
& $csc -nologo -target:winexe -out:"$root\dist\NenaPetSetup.exe" -optimize+ @iconArg "-resource:$zip,payload.zip" "-resource:$root\assets\splash.png,splash.png" @refs "$root\src\SetupProgram.cs"
if ($LASTEXITCODE -ne 0) { throw "fallo la compilacion del instalador" }
$mb = (Get-Item "$root\dist\NenaPetSetup.exe").Length / 1MB
Write-Host ("OK -> {0}\dist\NenaPetSetup.exe ({1:N2} MB)" -f $root, $mb)

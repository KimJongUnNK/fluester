# Fluester fuer Windows installieren oder aktualisieren - aus dem neuesten GitHub-Release.
#
#   irm https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.ps1 | iex
#
# Entfernen:
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.ps1))) -Uninstall
#
# Keine Administratorrechte noetig: Installation nach %LOCALAPPDATA%\Programs\Fluester.
# (Datei bewusst ohne Umlaute - Windows PowerShell 5.1 liest Skripte sonst falsch.)
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest ist sonst sehr langsam
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Repo = if ($env:FLUESTER_REPO) { $env:FLUESTER_REPO } else { 'KimJongUnNK/fluester' }
$Name = "Fl$([char]0x00FC)ster"           # Flüster
$Dest = Join-Path $env:LOCALAPPDATA 'Programs\Fluester'
$Exe = Join-Path $Dest 'Fluester.exe'
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$Name.lnk"
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Fluester'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Step($t) { Write-Host "> $t" -ForegroundColor White }
function Ok($t) { Write-Host "OK  $t" -ForegroundColor Green }
function Fail($t) { Write-Host "X   $t" -ForegroundColor Red; throw $t }

function Stop-Fluester {
    Get-Process -Name 'Fluester' -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(1500)) { $_.Kill() } } catch { }
    }
    Start-Sleep -Milliseconds 400
}

if ($Uninstall) {
    Step "Entferne $Name ..."
    Stop-Fluester
    Remove-ItemProperty -Path $RunKey -Name 'Fluester' -ErrorAction SilentlyContinue
    Remove-Item $Shortcut -ErrorAction SilentlyContinue
    Remove-Item $UninstallKey -Recurse -ErrorAction SilentlyContinue
    if (Test-Path $Dest) { Remove-Item $Dest -Recurse -Force }
    Ok "$Name entfernt."
    Write-Host "Einstellungen und Verlauf: $env:APPDATA\Fluester  -  Modelle: $env:LOCALAPPDATA\Fluester" -ForegroundColor DarkGray
    Write-Host "Ganz weg damit: Remove-Item -Recurse `"$env:APPDATA\Fluester`", `"$env:LOCALAPPDATA\Fluester`"" -ForegroundColor DarkGray
    return
}

if (-not [Environment]::Is64BitOperatingSystem) { Fail "$Name braucht ein 64-Bit-Windows." }
$build = [Environment]::OSVersion.Version.Build
if ($build -lt 17763) { Fail "$Name braucht Windows 10 (Version 1809) oder neuer." }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("fluester-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $zip = Join-Path $tmp 'Fluester.zip'
    if ($env:FLUESTER_ZIP) {
        Copy-Item $env:FLUESTER_ZIP $zip
    } else {
        Step 'Lade die neueste Version (ca. 70 MB) ...'
        $url = "https://github.com/$Repo/releases/latest/download/Fluester-Windows-x64.zip"
        try { Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing }
        catch { Fail "Download fehlgeschlagen. Internet pruefen oder manuell laden: https://github.com/$Repo/releases/latest" }
    }

    Step 'Entpacke ...'
    $out = Join-Path $tmp 'out'
    Expand-Archive -Path $zip -DestinationPath $out -Force
    if (-not (Test-Path (Join-Path $out 'Fluester.exe'))) { Fail 'Im Archiv fehlt Fluester.exe. Bitte spaeter erneut versuchen.' }

    $update = Test-Path $Exe
    Stop-Fluester

    Step "Installiere nach $Dest ..."
    if (Test-Path $Dest) { Remove-Item $Dest -Recurse -Force }
    New-Item -ItemType Directory -Path $Dest -Force | Out-Null
    Copy-Item -Path (Join-Path $out '*') -Destination $Dest -Recurse -Force
    # Herkunft "Internet" entfernen, damit SmartScreen nicht bei jedem Start fragt
    Get-ChildItem $Dest -Recurse -File | Unblock-File

    # Startmenue
    $wsh = New-Object -ComObject WScript.Shell
    $lnk = $wsh.CreateShortcut($Shortcut)
    $lnk.TargetPath = $Exe
    $lnk.WorkingDirectory = $Dest
    $lnk.Description = 'Diktieren in jeder App'
    $lnk.Save()

    # Deinstallation unter Einstellungen > Apps
    $uninstallScript = Join-Path $Dest 'uninstall.ps1'
    @"
`$ErrorActionPreference = 'SilentlyContinue'
Get-Process -Name 'Fluester' | Stop-Process -Force
Remove-ItemProperty -Path '$RunKey' -Name 'Fluester'
Remove-Item '$Shortcut'
Remove-Item '$UninstallKey' -Recurse
Start-Sleep -Milliseconds 500
Remove-Item '$Dest' -Recurse -Force
"@ | Set-Content -Path $uninstallScript -Encoding UTF8
    $version = (Get-Item $Exe).VersionInfo.ProductVersion
    New-Item -Path $UninstallKey -Force | Out-Null
    Set-ItemProperty -Path $UninstallKey -Name 'DisplayName' -Value $Name
    Set-ItemProperty -Path $UninstallKey -Name 'DisplayVersion' -Value $version
    Set-ItemProperty -Path $UninstallKey -Name 'Publisher' -Value $Name
    Set-ItemProperty -Path $UninstallKey -Name 'DisplayIcon' -Value $Exe
    Set-ItemProperty -Path $UninstallKey -Name 'InstallLocation' -Value $Dest
    Set-ItemProperty -Path $UninstallKey -Name 'UninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstallScript`""
    Set-ItemProperty -Path $UninstallKey -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -Path $UninstallKey -Name 'NoRepair' -Value 1 -Type DWord
    Set-ItemProperty -Path $UninstallKey -Name 'URLInfoAbout' -Value "https://github.com/$Repo"

    if ($env:FLUESTER_NO_START -ne '1') { Start-Process -FilePath $Exe }

    Write-Host ''
    if ($update) { Ok "$Name $version ist aktualisiert." } else { Ok "$Name $version ist installiert." }
    Write-Host ''
    Write-Host "  $Name sitzt unten rechts im Infobereich der Taskleiste (ggf. unter dem Pfeil ^)."
    Write-Host '  Beim ersten Start fuehrt die Einrichtung durch Mikrofon, Taste und Modell.'
    Write-Host '  Danach: in ein Textfeld klicken, STRG RECHTS halten, sprechen, loslassen.'
    Write-Host ''
    Write-Host "Anleitung und Hilfe: https://github.com/$Repo/blob/main/docs/windows.md" -ForegroundColor DarkGray
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

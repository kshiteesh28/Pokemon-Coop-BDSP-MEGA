# Starts the co-op server with one double-click (run through host-server.bat).
#
# What it does, in order:
#   1. Builds coop-server.exe if Go is installed (so it is always up to date)
#   2. First run only: asks for your name, room code and password -> coop.ini
#   3. First run only: adds the Windows Firewall rules (one "Yes" prompt)
#   4. Shows the address your friends need and copies an invite to the clipboard
#   5. Keeps the laptop awake while the server runs
param([int]$Port = 4100)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$Host.UI.RawUI.WindowTitle = 'Pokemon Co-op Server'

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }
function Quit([string]$Text) {
    Say $Text 'Red'
    Read-Host 'Press Enter to close' | Out-Null
    exit 1
}

function Read-CoopIni([string]$Path) {
    $cfg = @{}
    if (Test-Path $Path) {
        foreach ($line in Get-Content $Path) {
            $t = $line.Trim()
            if ($t -eq '' -or $t.StartsWith('#') -or $t.StartsWith(';') -or $t.StartsWith('[')) { continue }
            $i = $t.IndexOf('=')
            if ($i -lt 1) { continue }
            $cfg[$t.Substring(0, $i).Trim().ToLower()] = $t.Substring($i + 1).Trim().Trim('"')
        }
    }
    return $cfg
}

function Write-CoopIni([string]$Path, [hashtable]$Cfg) {
    $text = "# Pokemon co-op settings (used by host-server, join and the game)`r`n" +
            "address  = $($Cfg.address)`r`n" +
            "room     = $($Cfg.room)`r`n" +
            "name     = $($Cfg.name)`r`n" +
            "password = $($Cfg.password)`r`n"
    [IO.File]::WriteAllText((Join-Path $Root $Path), $text)
}

Say ''
Say '  ==============================================' 'Cyan'
Say '         POKEMON CO-OP  -  HOST A SERVER' 'Cyan'
Say '  ==============================================' 'Cyan'
Say ''

# ---------------------------------------------------------------- 1. build
if (Get-Command go -ErrorAction SilentlyContinue) {
    Say '  Building the server...'
    & go build -o coop-server.exe ./cmd/coop-server
    if ($LASTEXITCODE -ne 0) { Quit '  Build failed (see the error above).' }
    & go build -o coop-sim.exe ./cmd/coop-sim
} elseif (-not (Test-Path '.\coop-server.exe')) {
    Quit '  coop-server.exe is missing and Go is not installed. Install Go from https://go.dev/dl/ and try again.'
}

# ---------------------------------------------------------------- 2. settings
$cfg = Read-CoopIni 'coop.ini'
if (-not $cfg.room) {
    Say '  First-time setup (saved in coop.ini, you only do this once)' 'Yellow'
    Say ''
    $name = Read-Host '  Your trainer name'
    if (-not $name) { $name = 'Host' }
    do {
        $room = Read-Host '  Room code for your group (letters/numbers, e.g. AAROH)'
    } until ($room -match '^[A-Za-z0-9_-]{1,16}$')
    $pw = Read-Host '  Server password (friends need it; press Enter for none)'
    $cfg = @{ address = "127.0.0.1:$Port"; room = $room.ToUpper(); name = $name; password = $pw }
    Write-CoopIni 'coop.ini' $cfg
    Say '  Saved to coop.ini' 'Green'
    Say ''
}
$room = $cfg.room.ToUpper()
$pw = $cfg.password

# ---------------------------------------------------------------- 3. firewall
$rules = @(
    @{ Name = "Coop server $Port";               Extra = '-Profile Private' },
    @{ Name = "Coop server $Port (Tailscale)";   Extra = '-RemoteAddress 100.64.0.0/10' }
)
$missing = @($rules | Where-Object { -not (Get-NetFirewallRule -DisplayName $_.Name -ErrorAction SilentlyContinue) })
if ($missing.Count -gt 0) {
    Say '  Windows needs permission once so friends can connect. Click "Yes" on the prompt.' 'Yellow'
    $cmd = ($missing | ForEach-Object {
        "New-NetFirewallRule -DisplayName '$($_.Name)' -Direction Inbound -Protocol TCP -LocalPort $Port $($_.Extra) -Action Allow | Out-Null"
    }) -join '; '
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
    try {
        Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile -EncodedCommand $enc"
        Say '  Firewall rules added.' 'Green'
    } catch {
        Say '  Skipped: friends may not be able to connect until the firewall rules are added.' 'Yellow'
    }
    Say ''
}

# ---------------------------------------------------------------- 4. addresses
$ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue)
$tailscale = $ips | Where-Object { $_.IPAddress -like '100.*' -and $_.InterfaceAlias -like '*Tailscale*' } |
    Select-Object -First 1 -ExpandProperty IPAddress
$lan = Get-NetIPConfiguration -ErrorAction SilentlyContinue |
    Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' -and $_.InterfaceAlias -notlike '*Tailscale*' } |
    ForEach-Object { $_.IPv4Address.IPAddress } | Select-Object -First 1

$invite = $null
Say '  Give your friends:' 'White'
if ($tailscale) {
    Say "     Address   $($tailscale):$Port      (anyone on your Tailscale)" 'Green'
    $invite = "$($tailscale):$Port"
} else {
    Say '     Tailscale is not running, so only people on your Wi-Fi can join.' 'Yellow'
}
if ($lan) {
    Say "     Address   $($lan):$Port      (same Wi-Fi only)" 'Green'
    if (-not $invite) { $invite = "$($lan):$Port" }
}
Say "     Room      $room" 'Green'
if ($pw) { Say "     Password  $pw" 'Green' } else { Say '     Password  (none)' 'Green' }

$public = Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { $_.NetworkCategory -eq 'Public' -and $_.InterfaceAlias -notlike '*Tailscale*' }
if ($public -and $lan) {
    Say ''
    Say "  Note: your Wi-Fi '$($public[0].Name)' is set to Public, so same-Wi-Fi friends are blocked." 'Yellow'
    Say '        Tailscale friends still work. To allow Wi-Fi friends: Settings > Network > Wi-Fi > Private.' 'Yellow'
}

if ($invite) {
    $friendIni = "# Pokemon co-op invite: save this as coop.ini next to join.bat`r`n" +
                 "address  = $invite`r`nroom     = $room`r`nname     = `r`npassword = $pw`r`n"
    [IO.File]::WriteAllText((Join-Path $Root 'invite-coop.ini'), $friendIni)
    try { Set-Clipboard -Value "Join my Pokemon co-op server!`r`nAddress: $invite`r`nRoom: $room`r`nPassword: $(if ($pw) { $pw } else { '(none)' })" } catch { }
    Say ''
    Say '  The invite is copied to your clipboard - paste it to your friends.' 'Cyan'
    Say '  (It is also saved as invite-coop.ini if they want the file.)' 'DarkGray'
}

# ---------------------------------------------------------------- 5. keep awake + run
try {
    Add-Type -Namespace Coop -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);'
    [Coop.Power]::SetThreadExecutionState([uint32]2147483649) | Out-Null   # stay awake while this window is open
    Say '  Your laptop will stay awake while this window is open (closing the lid may still sleep it).' 'DarkGray'
} catch { }

Say ''
Say '  Server running. Close this window or press Ctrl+C to stop it.' 'White'
Say '  ----------------------------------------------' 'DarkGray'
$env:COOP_PASSWORD = $pw
& .\coop-server.exe -listen ":$Port"
Say ''
Read-Host '  Server stopped. Press Enter to close' | Out-Null

# Connects to a friend's co-op server as a test player (run through join.bat).
# First run asks for the address, room, your name and the password -> coop.ini.
# Afterwards it connects straight away. Stays online until you close the window.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$Host.UI.RawUI.WindowTitle = 'Pokemon Co-op - Join'

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

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

function Ask-Settings([hashtable]$Old) {
    Say '  Ask your host for the address, room code and password.' 'Yellow'
    Say '  (Press Enter to keep the value in [brackets].)' 'DarkGray'
    Say ''
    $cfg = @{}
    foreach ($k in 'address', 'room', 'name', 'password') {
        $label = @{ address = 'Server address (e.g. 100.67.109.85:4100)'; room = 'Room code'; name = 'Your trainer name'; password = 'Password (Enter if none)' }[$k]
        $prompt = "  $label"
        if ($Old[$k]) { $prompt += " [$($Old[$k])]" }
        $v = Read-Host $prompt
        if (-not $v) { $v = $Old[$k] }
        $cfg[$k] = "$v".Trim()
    }
    if ($cfg.address -and $cfg.address -notmatch ':\d+$') { $cfg.address += ':4100' }
    $text = "# Pokemon co-op settings (used by join and the game)`r`n" +
            "address  = $($cfg.address)`r`nroom     = $($cfg.room)`r`nname     = $($cfg.name)`r`npassword = $($cfg.password)`r`n"
    [IO.File]::WriteAllText((Join-Path $Root 'coop.ini'), $text)
    Say '  Saved to coop.ini' 'Green'
    Say ''
    return $cfg
}

function Test-Server([string]$Address) {
    $i = $Address.LastIndexOf(':')
    if ($i -lt 1) { return $false }
    $client = New-Object Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($Address.Substring(0, $i), [int]$Address.Substring($i + 1))
        return ($task.Wait(3000) -and $client.Connected)
    } catch { return $false } finally { $client.Close() }
}

Say ''
Say '  ==============================================' 'Cyan'
Say '         POKEMON CO-OP  -  JOIN A SERVER' 'Cyan'
Say '  ==============================================' 'Cyan'
Say ''

if (Get-Command go -ErrorAction SilentlyContinue) {
    & go build -o coop-sim.exe ./cmd/coop-sim
    if ($LASTEXITCODE -ne 0) { Read-Host '  Build failed. Press Enter to close' | Out-Null; exit 1 }
} elseif (-not (Test-Path '.\coop-sim.exe')) {
    Say '  coop-sim.exe is missing. Put it in this folder (ask your host) or install Go.' 'Red'
    Read-Host '  Press Enter to close' | Out-Null
    exit 1
}

# A host's invite file works as-is.
if (-not (Test-Path 'coop.ini') -and (Test-Path 'invite-coop.ini')) {
    Copy-Item 'invite-coop.ini' 'coop.ini'
    Say '  Using invite-coop.ini' 'Green'
}

$cfg = Read-CoopIni 'coop.ini'
if (-not $cfg.address -or -not $cfg.room -or -not $cfg.name) { $cfg = Ask-Settings $cfg }

while ($true) {
    Say "  Connecting to $($cfg.address)  room $($cfg.room.ToUpper())  as $($cfg.name) ..." 'White'
    if (Test-Server $cfg.address) { break }
    Say ''
    Say "  Can't reach $($cfg.address)." 'Red'
    Say '  - Is the host''s server window open?' 'Yellow'
    Say '  - Over the internet: is Tailscale on (and did you accept the host''s share link)?' 'Yellow'
    Say '  - Same Wi-Fi: is the host''s Wi-Fi set to Private?' 'Yellow'
    Say ''
    $a = Read-Host '  [R]etry, [C]hange settings, or [Q]uit'
    if ($a -match '^[Qq]') { exit 1 }
    if ($a -match '^[Cc]') { $cfg = Ask-Settings $cfg }
}

Say '  Connected! Other players will appear below. Close this window to leave.' 'Green'
Say '  ----------------------------------------------' 'DarkGray'
& .\coop-sim.exe -config coop.ini -players 1 -duration 0
Say ''
Read-Host '  Disconnected. Press Enter to close' | Out-Null

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = if ($env:REPO) { $env:REPO } else { 'ryandeering/libraries-ireland-mcp' }
$version = if ($env:VERSION) { $env:VERSION } else { 'latest' }
$installDir = if ($env:INSTALL_DIR) { $env:INSTALL_DIR } else {
    Join-Path $env:LOCALAPPDATA 'Programs\libraries-ireland-mcp'
}
$allowUnverified = $env:ALLOW_UNVERIFIED -eq '1'
$binName = 'libraries-ireland-mcp.exe'

function Say([string] $message) { Write-Host $message }
function Die([string] $message) { Write-Error $message; exit 1 }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$arch = $env:PROCESSOR_ARCHITECTURE
switch ($arch) {
    'AMD64' { $asset = 'libraries-ireland-mcp-win-x64.exe' }
    'ARM64' {
        $asset = 'libraries-ireland-mcp-win-x64.exe'
        Say 'Windows on ARM detected. Installing the x64 build, which runs under emulation.'
    }
    default { Die "unsupported processor architecture: $arch. Only x64 is published." }
}

$base = if ($version -eq 'latest') {
    "https://github.com/$repo/releases/latest/download"
} else {
    "https://github.com/$repo/releases/download/$version"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $temp -Force | Out-Null

try {
    $downloaded = Join-Path $temp $binName

    Say "Downloading $asset ($version)..."
    try {
        Invoke-WebRequest -Uri "$base/$asset" -OutFile $downloaded -UseBasicParsing
    } catch {
        Die "could not download $base/$asset : $($_.Exception.Message)"
    }

    $sumFile = Join-Path $temp 'sum'
    $havePublishedSum = $true
    try {
        Invoke-WebRequest -Uri "$base/$asset.sha256" -OutFile $sumFile -UseBasicParsing
    } catch {
        $havePublishedSum = $false
    }

    if ($havePublishedSum) {
        $expected = ((Get-Content -Raw $sumFile).Trim() -split '\s+')[0]
        $actual = (Get-FileHash -Path $downloaded -Algorithm SHA256).Hash

        if ($expected -ine $actual) {
            Die "checksum mismatch: expected $expected, got $actual"
        }

        Say 'Checksum verified.'
    } elseif ($allowUnverified) {
        Say "warning: no published checksum for $asset, continuing because ALLOW_UNVERIFIED=1"
    } else {
        Die ("no published checksum for $asset, so the download cannot be verified. " +
             "Re-run with `$env:ALLOW_UNVERIFIED='1' to install anyway.")
    }

    Unblock-File -Path $downloaded -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Path $installDir -Force | Out-Null

    $installDir = (Resolve-Path -LiteralPath $installDir).Path
    $target = Join-Path $installDir $binName
    Move-Item -Path $downloaded -Destination $target -Force

    Say ''
    Say "Installed to $target"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($null -eq $userPath) { $userPath = '' }

    $matches = @($userPath -split ';' | Where-Object { $_.TrimEnd('\') -ieq $installDir.TrimEnd('\') })
    $onPath = $matches.Count -gt 0
    if (-not $onPath) {
        $separator = if ($userPath -eq '' -or $userPath.EndsWith(';')) { '' } else { ';' }
        [Environment]::SetEnvironmentVariable('Path', "$userPath$separator$installDir", 'User')
        Say ''
        Say "Added $installDir to your PATH. Open a new terminal for that to take effect."
    }

    Say ''
    Say 'Next, register it with your MCP client.'
    Say ''
    Say '  Claude Code:'
    Say "    claude mcp add libraries-ireland `"$target`""
    Say ''
    Say '  Codex:'
    Say "    codex mcp add libraries-ireland -- `"$target`""
    Say ''
    Say 'Then tell it which library you use, for example:'
    Say '    "I''m with Dublin City libraries, I usually go to Ballyfermot."'
} finally {
    Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
}

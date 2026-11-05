param(
    [string]$UnityPath = "$env:ProgramFiles/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe",
    [switch]$SkipPlayerBuild,
    [string]$LauncherOutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$player = Join-Path $root 'Build/Windows'
$launcher = if ($LauncherOutputDirectory) { [IO.Path]::GetFullPath((Join-Path $root $LauncherOutputDirectory)) } else { Join-Path $root 'Build/Launcher' }
$log = Join-Path $root 'Logs/release-player.log'
Push-Location $root
try {
    if (-not $SkipPlayerBuild) {
        if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity not found: $UnityPath" }
        [IO.Directory]::CreateDirectory((Split-Path -Parent $log)) | Out-Null
        $arguments = '-batchmode -nographics -quit -projectPath "' + $root + '" -executeMethod HalliGalli.EditorTools.RaceCourseBuilder.BuildPlayer -logFile "' + $log + '"'
        $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Unity build failed ($($process.ExitCode)); see $log" }
    }
    $stampPath = Join-Path $player 'BellRun-build.json'
    if (-not (Test-Path -LiteralPath $stampPath)) { throw 'Missing embedded build stamp. Build the release-gated player first.' }
    $stamp = Get-Content -LiteralPath $stampPath -Raw | ConvertFrom-Json
    if ($stamp.schemaVersion -ne 1 -or $stamp.version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or $stamp.protocol -isnot [int] -or $stamp.protocol -lt 1 -or $stamp.entryPoint -ne 'HalliGalliMovement.exe') { throw 'Unsupported player build stamp.' }
    foreach ($required in @('HalliGalliMovement.exe','UnityPlayer.dll','HalliGalliMovement_Data/globalgamemanagers','HalliGalliMovement_Data/Managed/Assembly-CSharp.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $player $required) -PathType Leaf)) { throw "Missing player file: $required" }
    }
    & dotnet publish 'Launcher/BellRun.Launcher/BellRun.Launcher.csproj' -c Release -r win-x64 --self-contained true -o $launcher
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
    $version = $stamp.version
    $output = Join-Path $root "Build/Releases/$version"
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $archiveName = "BellRun-Windows-$version.zip"
    $archivePath = Join-Path $output $archiveName
    $temporaryArchive = $archivePath + '.partial'
    if (Test-Path -LiteralPath $temporaryArchive) { throw "Previous unfinished package exists: $temporaryArchive" }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($temporaryArchive, [IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $prefix = $player.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
            $count = 0
            foreach ($file in Get-ChildItem -LiteralPath $player -File -Recurse) {
                $relative = $file.FullName.Substring($prefix.Length).Replace('\','/')
                if ($relative -match '(^|/)[^/]*_BackUpThisFolder_ButDontShipItWithYourGame(/|$)' -or $relative -match '\.(pdb|log|tmp)$') { continue }
                if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point in player: $relative" }
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
                $count++
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
    $digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $version
        protocol = [int]$stamp.protocol
        archiveName = $archiveName
        downloadUrl = "https://github.com/alphamon1114/halligalli-deathrun/releases/download/v$version/$archiveName"
        sha256 = $digest
        size = (Get-Item -LiteralPath $archivePath).Length
        entryPoint = $stamp.entryPoint
    }
    [IO.File]::WriteAllText((Join-Path $output 'BellRun-release.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $launcher 'BellRunLauncher.exe') -Destination $output -Force
    Write-Output "Package: $output"
    Write-Output "Version: $version; protocol: $($stamp.protocol); files: $count; SHA256: $digest"
    Write-Output 'Not published. Commit/push the matching source, then use Tools/Release/Publish-Release.ps1.'
} finally { Pop-Location }

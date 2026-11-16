param(
    [string]$UnityPath = "$env:ProgramFiles/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe",
    [switch]$SkipPlayerBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$player = Join-Path $root 'Build/OfflineDemo'
$config = Join-Path $PSScriptRoot 'OfflineDemoConfig'
Push-Location $root
try {
    if (-not $SkipPlayerBuild) {
        if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity not found: $UnityPath" }
        [IO.Directory]::CreateDirectory((Join-Path $root 'Logs')) | Out-Null
        $log = Join-Path $root 'Logs/offline-demo-build.log'
        $arguments = '-batchmode -nographics -quit -projectPath "' + $root + '" -executeMethod HalliGalli.EditorTools.OfflineDemoBuild.Build -logFile "' + $log + '"'
        $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Offline demo build failed ($($process.ExitCode)); see $log" }
    }
    $stamp = Get-Content -LiteralPath (Join-Path $player 'HalliGalliRunDemo-build.json') -Raw | ConvertFrom-Json
    if ($stamp.schemaVersion -ne 1 -or $stamp.edition -ne 'offline-demo' -or $stamp.requiresInternet -ne $false -or $stamp.onlineMultiplayer -ne $false -or $stamp.entryPoint -ne 'HalliGalliRunDemo.exe' -or $stamp.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Not a valid offline-demo build.' }
    if (Test-Path -LiteralPath (Join-Path $player 'BellRun-build.json')) { throw 'Offline output contains public release metadata.' }
    foreach ($required in @($stamp.entryPoint,'UnityPlayer.dll','HalliGalliRunDemo_Data/globalgamemanagers','HalliGalliRunDemo_Data/Managed/Assembly-CSharp.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $player $required) -PathType Leaf)) { throw "Missing demo file: $required" }
    }
    $utf8 = [Text.UTF8Encoding]::new($false,$true)
    foreach ($name in @('exe','title','genre','playtime','description','team')) {
        if ([string]::IsNullOrWhiteSpace($utf8.GetString([IO.File]::ReadAllBytes((Join-Path $config "$name.txt"))))) { throw "Empty config: $name" }
    }
    if ([IO.File]::ReadAllText((Join-Path $config 'exe.txt'),$utf8).Trim() -ne $stamp.entryPoint) { throw 'Config executable mismatch.' }
    Add-Type -AssemblyName System.Drawing
    foreach ($name in @('thumbnail','image1','image2','image3')) {
        $image = [Drawing.Image]::FromFile((Join-Path $config "$name.png"))
        try {
            $w = if ($name -eq 'thumbnail') {840} else {1920}
            $h = if ($name -eq 'thumbnail') {300} else {1200}
            if ($image.Width -ne $w -or $image.Height -ne $h) { throw "Wrong image dimensions: $name" }
        } finally { $image.Dispose() }
    }
    $output = Join-Path $root ('Exports/GSTAR2026_OfflineDemo_' + $stamp.version)
    $archive = $output + '.zip'
    if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $archive)) { throw "Existing submission preserved; choose a new version or move the old package first: $output" }
    $game = Join-Path $output '01_HALLIGALLI_RUN_Offline'
    [IO.Directory]::CreateDirectory($game) | Out-Null
    $prefix = $player.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in Get-ChildItem -LiteralPath $player -File -Recurse) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\','/')
        if ($relative -match '(^|/)[^/]*_BackUpThisFolder_ButDontShipItWithYourGame(/|$)' -or $relative -match '\.(pdb|log|tmp)$') { continue }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point in demo: $relative" }
        $destination = Join-Path $game $relative
        [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        [IO.File]::Copy($file.FullName,$destination)
    }
    Copy-Item -LiteralPath $config -Destination (Join-Path $game 'config') -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OfflineDemo_README.txt') -Destination (Join-Path $output 'READ_ME.txt')
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
    try {
        $prefix = $output.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $output -File -Recurse) {
            $relative = $file.FullName.Substring($prefix.Length).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    $sha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "Offline demo: $archive"
    Write-Output "Version: $($stamp.version); bytes: $((Get-Item -LiteralPath $archive).Length); SHA256: $sha"
    Write-Output 'Independent exhibition artifact only. Public release and launcher are not published or updated.'
} finally { Pop-Location }

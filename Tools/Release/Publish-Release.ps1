param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$NotesFile
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Use a numeric major.minor.patch release version.' }
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$repo = 'alphamon1114/halligalli-deathrun'
$tag = 'v' + $Version
$folder = Join-Path $root "Build/Releases/$Version"
$manifestPath = Join-Path $folder 'BellRun-release.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$archiveName = "BellRun-Windows-$Version.zip"
$archive = Join-Path $folder $archiveName
$launcher = Join-Path $folder 'BellRunLauncher.exe'
if ($manifest.schemaVersion -ne 1 -or $manifest.version -ne $Version -or $manifest.protocol -isnot [int] -or $manifest.protocol -lt 1 -or $manifest.entryPoint -ne 'HalliGalliMovement.exe' -or $manifest.archiveName -ne $archiveName -or $manifest.downloadUrl -ne "https://github.com/$repo/releases/download/$tag/$archiveName") { throw 'Release manifest does not match this repository/version.' }
if ((Get-Item -LiteralPath $archive).Length -ne $manifest.size -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.sha256) { throw 'Release archive hash or size mismatch.' }
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw 'Launcher executable missing.' }
Push-Location $root
try {
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit all source changes before publishing a release.' }
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit.' }
    $remoteCommit = & gh api "repos/$repo/commits/main" --jq '.sha'
    if ($LASTEXITCODE -ne 0 -or $remoteCommit -ne $commit) { throw 'Push the release source to origin/main before publishing.' }
    $arguments = @('release','create',$tag,'--repo',$repo,'--target',$commit,'--title',"할리갈리런 $Version",'--draft',$archive,$manifestPath,$launcher)
    if ($NotesFile) { $arguments += @('--notes-file', (Resolve-Path -LiteralPath $NotesFile).Path) }
    else { $arguments += @('--notes',"할리갈리런 설치·업데이트는 BellRunLauncher.exe를 실행하세요. 인터넷 연결과 최신 버전이 필요하며 사용자 맵과 설정은 보존됩니다. 버전 $Version / 네트워크 프로토콜 $($manifest.protocol). Windows x64.") }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Draft creation/upload failed. Inspect the draft; do not replace an existing published version.' }
    $assetNames = & gh release view $tag --repo $repo --json assets --jq '.assets[].name'
    if ($LASTEXITCODE -ne 0 -or $assetNames -notcontains $archiveName -or $assetNames -notcontains 'BellRun-release.json' -or $assetNames -notcontains 'BellRunLauncher.exe') { throw 'Draft assets incomplete; release remains unpublished.' }
    & gh release edit $tag --repo $repo --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; inspect GitHub release state.' }
    Write-Output "Published https://github.com/$repo/releases/tag/$tag at source $commit"
} finally { Pop-Location }

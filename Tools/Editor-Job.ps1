param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('pyramid','pyramid-probe','pyramid-validate','pyramid-capture','pyramid-ui','capture-build','open')]
    [string]$Job
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$jobFolder=Join-Path (Split-Path -Parent $projectRoot) 'outputs/course-jobs'
$request=Join-Path $jobFolder 'request.txt'
if(Test-Path -LiteralPath $request){throw 'An editor job is already queued. Wait for it to be consumed.'}
[IO.Directory]::CreateDirectory($jobFolder)|Out-Null
[IO.File]::WriteAllText($request,$Job,[Text.UTF8Encoding]::new($false))
Write-Output ('Queued '+$Job+'. Unity must have this project open.')
Write-Output ('Status: '+(Join-Path $jobFolder 'status.txt'))
Write-Output 'A DISPATCHED status does not mean asynchronous validation has finished. Check TestResults and the Unity log before queuing another job.'

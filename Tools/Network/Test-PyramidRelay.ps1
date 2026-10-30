param([switch]$Relay,[switch]$Visual,[string]$NodePath='node')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runRoot=Join-Path (Join-Path $PSScriptRoot 'Runs') ('compact-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$gameExe=Join-Path $projectRoot 'Build/Windows/HalliGalliMovement.exe'

$nodeExe=(Get-Command $NodePath -ErrorAction Stop).Source
if(!(Test-Path -LiteralPath $gameExe)){throw 'Build/Windows/HalliGalliMovement.exe is missing. Build the project first.'}
$ownedProcesses=[Collections.Generic.List[object]]::new()
function Start-Probe([string]$role) {
    $arguments=@('-batchmode','-nographics','--net-probe',$role,'--probe-dir',('"'+$runRoot+'"'),'--net-port','17879','-logFile',('"'+(Join-Path $runRoot ($role+'.log'))+'"'))
    if ($Visual -and $role -eq 'host') {$arguments=@($arguments | Where-Object {$_ -ne '-batchmode' -and $_ -ne '-nographics'})}
    if ($Relay) {$arguments+=@('--relay-probe','true','--auth-profile',('compact'+$role))}
    $process=Start-Process -FilePath $gameExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $ownedProcesses.Add($process)
}
try {
    Start-Probe 'host'
    $deadline=(Get-Date).AddSeconds(35)
    while (!(Test-Path (Join-Path $runRoot 'state-host.json')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if ($Relay) {
        $hostState=$null
        do {
            try {$hostState=Get-Content (Join-Path $runRoot 'state-host.json') -Raw|ConvertFrom-Json} catch {}
            if ($hostState.code) {break}
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)
        if (!$hostState.code) {throw ('Relay room creation failed: '+$hostState.status+' '+$hostState.error)}
    }
    Start-Probe 'client1'
    if ($Relay) {
        [pscustomobject]@{sequence=1;action='join';value=$hostState.code} | ConvertTo-Json | Set-Content (Join-Path $runRoot 'command-client1.json')
    }
    $ownedProcesses | Select-Object Id,ProcessName | ConvertTo-Json | Set-Content (Join-Path $runRoot 'processes.json')
    $testScript='check-pyramid.mjs'
    & $nodeExe (Join-Path $PSScriptRoot $testScript) $runRoot $projectRoot
    $testExit=$LASTEXITCODE
    $resultFile='pyramid-results.json'
    $reportFile='TestResults/pyramid-network-validation.json'
    Copy-Item -LiteralPath (Join-Path $runRoot $resultFile) -Destination (Join-Path $projectRoot $reportFile) -Force
    if ($testExit -ne 0) {throw 'Compact build multiplayer validation failed'}
    Write-Output ('COMPACT_NETWORK_PASSED '+$runRoot)
} finally {
    try {
        foreach ($role in @('client1','host')) {
            $commandPath=Join-Path $runRoot ('command-'+$role+'.json')
            $commandText=[pscustomobject]@{sequence=100000;action='leave'} | ConvertTo-Json
            for($retry=0;$retry -lt 20;$retry++) {
                try {[IO.File]::WriteAllText($commandPath,$commandText);break}
                catch [IO.IOException] {Start-Sleep -Milliseconds 100}
            }
        }
        Start-Sleep -Seconds 2
    } finally {
        foreach ($process in $ownedProcesses) {
            $running=Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($running -and $running.ProcessName -eq 'HalliGalliMovement') { Stop-Process -Id $running.Id }
        }
    }
}

param(
    [ValidateSet("ThunderstoreApp", "Steam")]
    [string]$LaunchMode = "ThunderstoreApp",

    [switch]$SkipCompanion
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginBuildDir = Join-Path $repoRoot "src\ErenshorBuddy.Plugin\bin\Debug\net48"
$companionExe = Join-Path $repoRoot "src\ErenshorBuddy.Companion\bin\Debug\net8.0-windows\ErenshorBuddy.Companion.exe"

$profilePluginDir = "C:\Users\Hunter\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Erenshor\profiles\Default\BepInEx\plugins\ErenshorBuddy"
$runtimeDir = Join-Path $env:AppData "ErenshorBuddy\Runtime"
$overwolfLauncher = "C:\Program Files (x86)\Overwolf\OverwolfLauncher.exe"
$thunderstoreAppId = "ahpflogoookodlegojjphcjpjaejgghjnfcdjdmi"
$steamLaunchUri = "steam://rungameid/2382520"

function Stop-ProcessIfRunning {
    param([string]$Name)

    Get-Process -Name $Name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Sync-PluginOutput {
    $files = @(
        "ErenshorBuddy.Plugin.dll",
        "ErenshorBuddy.Core.dll",
        "ErenshorBuddy.Contracts.dll",
        "Newtonsoft.Json.dll"
    )

    New-Item -ItemType Directory -Force -Path $profilePluginDir | Out-Null

    foreach ($file in $files) {
        $src = Join-Path $pluginBuildDir $file
        $dst = Join-Path $profilePluginDir $file
        [System.IO.File]::Copy($src, $dst, $true)
    }
}

function Reset-RuntimeFiles {
    $commandsDir = Join-Path $runtimeDir "commands"
    New-Item -ItemType Directory -Force -Path $commandsDir | Out-Null

    Get-ChildItem $commandsDir -Filter *.json -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    foreach ($name in @("status.json", "snapshot.json", "heartbeat.json", "events.log")) {
        $path = Join-Path $runtimeDir $name
        if (Test-Path $path) {
            Clear-Content $path -ErrorAction SilentlyContinue
        }
    }
}

Stop-ProcessIfRunning -Name "Erenshor"
Stop-ProcessIfRunning -Name "ErenshorBuddy.Companion"

Sync-PluginOutput
Reset-RuntimeFiles

if (-not $SkipCompanion -and (Test-Path $companionExe)) {
    Start-Process $companionExe | Out-Null
}

switch ($LaunchMode) {
    "ThunderstoreApp" {
        if (Test-Path $overwolfLauncher) {
            Start-Process $overwolfLauncher "-launchapp $thunderstoreAppId" | Out-Null
        }
        else {
            throw "Overwolf launcher was not found at '$overwolfLauncher'."
        }
    }

    "Steam" {
        Start-Process $steamLaunchUri | Out-Null
    }
}

Write-Host "Retest environment prepared."
Write-Host "Launch mode: $LaunchMode"
Write-Host "Plugin synced to: $profilePluginDir"
Write-Host "Runtime directory reset: $runtimeDir"

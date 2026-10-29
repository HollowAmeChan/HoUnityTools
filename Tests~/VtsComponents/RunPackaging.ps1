# Creates/uses only a marked disposable project. Does not build into the Warudo installation.
param(
    [Parameter(Mandatory=$true)][string] $Project,
    [string] $SourceProject = 'D:\Unity_Project\BreakWarudo',
    [string] $Unity = 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe',
    [string] $ModCore
)
$ErrorActionPreference = 'Stop'
$Project = [System.IO.Path]::GetFullPath($Project)
$SourceProject = (Resolve-Path -LiteralPath $SourceProject).Path
$marker = Join-Path $Project '.ho-vts-packaging-validation'
if (Test-Path -LiteralPath $Project) {
    if (-not (Test-Path -LiteralPath $marker)) { throw 'Disposable project marker missing.' }
} else {
    New-Item -ItemType Directory -Path $Project | Out-Null
    Set-Content -LiteralPath $marker -Value 'Disposable VTS SDK validation project'
}
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$editor = Join-Path $Project 'Assets\Editor\VtsPackaging'
New-Item -ItemType Directory -Force -Path $editor, (Join-Path $Project 'Packages'), (Join-Path $Project 'ProjectSettings') | Out-Null
$sourceManifest = Get-Content -LiteralPath (Join-Path $SourceProject 'Packages\manifest.json') -Raw | ConvertFrom-Json
$dependencies = [ordered]@{}
foreach ($entry in $sourceManifest.dependencies.PSObject.Properties) {
    if ($entry.Name.StartsWith('com.unity.modules.') -or $entry.Name -in @(
        'app.warudo.modtool', 'com.unity.ide.visualstudio', 'com.unity.timeline', 'com.unity.textmeshpro')) {
        $dependencies[$entry.Name] = $entry.Value
    }
}
if (-not $dependencies.Contains('app.warudo.modtool')) { throw 'Source project has no local Warudo SDK package.' }
$dependencies['com.hollow.hounitytools'] = 'file:' + $packageRoot.Replace('\', '/')
# Prefer the already installed package when available; the source SDK uses TMP types unconditionally.
$tmpVersion = [string]$sourceManifest.dependencies.'com.unity.textmeshpro'
$tmpCache = Join-Path $SourceProject ('Library\PackageCache\com.unity.textmeshpro@' + $tmpVersion)
if (Test-Path -LiteralPath (Join-Path $tmpCache 'package.json')) {
    $dependencies['com.unity.textmeshpro'] = 'file:' + $tmpCache.Replace('\', '/')
}
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $Project 'Packages\manifest.json'),
    (@{ dependencies = $dependencies } | ConvertTo-Json -Depth 10), $utf8)
Copy-Item -LiteralPath (Join-Path $SourceProject 'ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $Project 'ProjectSettings\ProjectVersion.txt')
if (-not (Test-Path -LiteralPath (Join-Path $Project 'ProjectSettings\ProjectSettings.asset'))) {
    $settings = Get-Content -LiteralPath (Join-Path $SourceProject 'ProjectSettings\ProjectSettings.asset') -Raw
    $settings = [regex]::Replace($settings, '(?m)^  companyName:.*$', '  companyName: HollowValidation')
    $settings = [regex]::Replace($settings, '(?m)^  productName:.*$', '  productName: HoVtsPackagingValidation')
    $settings = [regex]::Replace($settings, '(?m)^  cloudProjectId:.*$', '  cloudProjectId: ')
    [System.IO.File]::WriteAllText((Join-Path $Project 'ProjectSettings\ProjectSettings.asset'), $settings, $utf8)
}
Copy-Item -LiteralPath (Join-Path $SourceProject 'Assets\ExportSettings.asset') -Destination (Join-Path $Project 'Assets\ExportSettings.asset')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PackagingValidation.cs') -Destination $editor
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AuthoringValidation.cs') -Destination $editor
if ([string]::IsNullOrEmpty($ModCore)) { $ModCore = Join-Path $SourceProject 'Assets\HoWarudoModTests\Mods-Ho\HoFaceTracking\Core' }
$delivery = Join-Path $Project 'Assets\Delivery'
New-Item -ItemType Directory -Force -Path $delivery | Out-Null
Copy-Item -LiteralPath (Join-Path $ModCore 'HoVtsFrameDelivery.cs') -Destination $delivery
[System.IO.File]::WriteAllText((Join-Path $delivery 'HoVtsDeliveryValidation.asmdef'), '{"name":"HoVtsDeliveryValidation","references":[],"autoReferenced":true}', $utf8)
$assembly = @{ name = 'VtsPackagingValidation'; references = @('HoUnityTools.Runtime', 'HoUnityTools.Editor', 'HoVtsDeliveryValidation'); includePlatforms = @('Editor') }
[System.IO.File]::WriteAllText((Join-Path $editor 'VtsPackagingValidation.asmdef'), ($assembly | ConvertTo-Json), $utf8)
$report = Join-Path $Project 'vts-packaging-result.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$log = Join-Path $Project 'vts-packaging.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'),
    '-executeMethod', 'HoVtsPackagingValidation.RunBatch', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity packaging validation PID: $($process.Id)"
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String 'HO_VTS_AUTHORING|HO_VTS_PACKAGING|error CS|Illegal reference|validation failed'
if ($process.ExitCode -ne 0) { throw "Unity packaging validation failed ($($process.ExitCode)); see $log" }
if (-not (Test-Path -LiteralPath $report) -or -not (Select-String -LiteralPath $report -Pattern '^HO_VTS_PACKAGING PASS' -Quiet)) {
    throw 'Packaging PASS report missing.'
}
Get-Content -LiteralPath $report
$runtimeReport = Join-Path $Project 'vts-mod-runtime-result.txt'
if (Test-Path -LiteralPath $runtimeReport) { Remove-Item -LiteralPath $runtimeReport }
$runtimeLog = Join-Path $Project 'vts-mod-runtime.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $Project + '"'),
    '-executeMethod', 'HoVtsPackagingValidation.RunLoadBatch', '-logFile', ('"' + $runtimeLog + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity artifact runtime validation PID: $($process.Id)"
$process.WaitForExit()
Get-Content -LiteralPath $runtimeLog | Select-String 'HO_VTS_AUTHORING|HO_VTS_PACKAGING|HO_VTS_MOD_RUNTIME|error CS|Exception:'
if ($process.ExitCode -ne 0) { throw "UMod runtime validation failed ($($process.ExitCode)); see $runtimeLog" }
if (-not (Test-Path -LiteralPath $runtimeReport) -or -not (Select-String -LiteralPath $runtimeReport -Pattern '^HO_VTS_MOD_RUNTIME PASS' -Quiet)) {
    throw 'Mod runtime PASS report missing.'
}
Get-Content -LiteralPath $runtimeReport

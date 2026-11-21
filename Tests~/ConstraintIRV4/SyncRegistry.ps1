param([Parameter(Mandatory=$true)][string]$Authority,[switch]$Check)
$ErrorActionPreference='Stop'
$registryRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$registryDestination=Join-Path $registryRepo 'Editor/HoFbxImport/ConstraintIR/Registry/registry.v1.json'
$registryBytes=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Authority))
if($Check){
    if(-not(Test-Path -LiteralPath $registryDestination)){throw 'Distributed registry is missing.'}
    $registryOther=[IO.File]::ReadAllBytes($registryDestination)
    if([Convert]::ToBase64String($registryBytes) -cne [Convert]::ToBase64String($registryOther)){throw 'Distributed registry drifted from the Blender authority.'}
}else{
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($registryDestination))|Out-Null
    [IO.File]::WriteAllBytes($registryDestination,$registryBytes)
}
Write-Output "Registry bytes match authority: $registryDestination"

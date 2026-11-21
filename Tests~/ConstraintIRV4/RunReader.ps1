param([Parameter(Mandatory=$true)][string]$Registry,[string]$Fixture='')
$ErrorActionPreference='Stop'
$readerProject=Join-Path $PSScriptRoot 'ReaderValidation.csproj'
if($Fixture){ & dotnet run --project $readerProject -- $Registry $Fixture }
else { & dotnet run --project $readerProject -- $Registry }
if($LASTEXITCODE -ne 0){throw 'IR v4 reader/registry/projection validation failed.'}

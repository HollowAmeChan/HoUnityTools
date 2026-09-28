# Mirror the package into .research/pkgcopy for the disposable validation project.
# NOTE: ASCII only -- PowerShell reads .ps1 as ANSI unless it has a BOM, which mangles Chinese.
#
# History: this script used to gut Editor/AnimationTools + Runtime/AnimationTools and keep only
# the blend-shape builder, because another worker's in-flight files up there broke the shared
# package compile. That work has landed and the whole package now compiles with those folders
# included (compile-check-pkg.ps1 checks them again), so the mirror is a straight /MIR.
$src = "D:\Unity_Fork\HoUnityTools"
$dst = "$src\.research\pkgcopy"
$skipDirs = @("$src\Tools~", "$src\.research", "$src\.git", "$src\Library", "$src\Temp", "$src\obj")

New-Item -ItemType Directory -Force -Path $dst | Out-Null
robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NP /XD $skipDirs /XF "*.csproj" "*.sln" | Out-Null

$files = (Get-ChildItem $dst -Recurse -File | Measure-Object).Count
Write-Output ("pkgcopy: " + $files + " files (straight mirror)")

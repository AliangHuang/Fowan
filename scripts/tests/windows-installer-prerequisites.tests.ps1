param()

$ErrorActionPreference = "Stop"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installer = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot "installer/windows/Fowan.iss")
$packageScript = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot "scripts/package-windows.ps1")

Assert-True (-not $installer.Contains('DotNetDesktopRuntimePath')) "installer must not require a bundled .NET runtime"
Assert-True (-not $installer.Contains('WindowsAppRuntimeInstallerPath')) "installer must not require a bundled Windows App Runtime"
Assert-True (-not $installer.Contains('VcRedistPath')) "installer must not require a bundled VC++ runtime"
Assert-True (-not $installer.Contains('DestName: "windowsdesktop-runtime-8-x64.exe"')) "installer must not embed the .NET runtime installer"
Assert-True (-not $installer.Contains('DestName: "WindowsAppRuntimeInstall-x64.exe"')) "installer must not embed the Windows App Runtime installer"
Assert-True (-not $installer.Contains('DestName: "vc_redist.x64.exe"')) "installer must not embed the VC++ runtime installer"
Assert-True ($installer.Contains('procedure DetectMissingPrerequisites();')) "installer must detect missing prerequisites before installation"
Assert-True ($installer.Contains('function HasWindowsAppRuntime2(): Boolean;')) "installer must detect the Windows App Runtime"
Assert-True ($installer.Contains('procedure OpenMissingPrerequisiteDownloads();')) "installer must offer official prerequisite downloads"
Assert-True ($installer.Contains('if not WizardSilent() then')) "installer must not show download prompts during silent installation"
Assert-True ($installer.Contains('MissingPrerequisitesText()')) "installer must report missing prerequisites before copying application files"
Assert-True ($installer.Contains('Microsoft.WindowsDesktop.App.deps.json')) "installer must recognize the actual .NET Desktop Runtime metadata file"
Assert-True (-not $installer.Contains('Microsoft.WindowsDesktop.App.dll')) "installer must not require a non-existent .NET Desktop Runtime DLL"
Assert-True ($installer.Contains('System.Private.CoreLib.dll')) "installer must verify the matching .NET runtime host library"
Assert-True (-not $packageScript.Contains("Ensure-RuntimePrerequisites")) "package build must not download prerequisites"
Assert-True (-not $packageScript.Contains("windowsdesktop-runtime-8-x64.exe")) "package build must not stage the .NET runtime"
Assert-True (-not $packageScript.Contains("WindowsAppRuntimeInstall-x64.exe")) "package build must not stage the Windows App Runtime"
Assert-True (-not $packageScript.Contains("vc_redist.x64.exe")) "package build must not stage the VC++ runtime"

Write-Host "Windows installer prerequisite regression checks passed."

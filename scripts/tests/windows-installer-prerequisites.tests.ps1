param()

$ErrorActionPreference = "Stop"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installer = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot "installer/windows/Fowan.iss")
$packageScript = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot "scripts/package-windows.ps1")

Assert-True (-not $installer.Contains('DotNetDesktopRuntimePath')) "installer must not require a machine-level .NET runtime"
Assert-True (-not $installer.Contains('WindowsAppRuntimeInstallerPath')) "installer must not require a machine-level Windows App Runtime"
Assert-True ($installer.Contains('VcRedistPath')) "installer must require a bundled VC++ runtime"
Assert-True ($installer.Contains('Source: "{#VcRedistPath}"; Flags: dontcopy noencryption')) "installer must embed the VC++ runtime for pre-install extraction"
Assert-True ($installer.Contains('procedure DetectMissingPrerequisites();')) "installer must detect missing prerequisites before installation"
Assert-True (-not $installer.Contains('HasDotNetDesktopRuntime8')) "installer must not gate installation on .NET discovery"
Assert-True (-not $installer.Contains('HasWindowsAppRuntime2')) "installer must not gate installation on Windows App Runtime discovery"
Assert-True ($installer.Contains('function InstallMissingPrerequisites(var NeedsRestart: Boolean): String;')) "installer must install only missing prerequisites"
Assert-True ($installer.Contains("ExtractTemporaryFile('vc_redist.x64.exe')")) "installer must extract the bundled VC++ runtime before app files are copied"
Assert-True ($installer.Contains("if ResultCode = 3010 then")) "installer must handle prerequisite restart-required success"
Assert-True ($installer.Contains('PrerequisiteRestartRequired := True')) "installer must remember when a prerequisite requires restart"
Assert-True ($installer.Contains('Check: ShouldRunFowanAfterInstall')) "installer must not launch Fowan before a required restart"
Assert-True ($packageScript.Contains("Ensure-RuntimePrerequisites")) "package build must acquire prerequisites"
Assert-True ($packageScript.Contains("Assert-MicrosoftSignature")) "package build must validate Microsoft Authenticode signatures"
Assert-True ($packageScript.Contains("--self-contained true")) "package build must publish .NET self-contained applications"
Assert-True ($packageScript.Contains("WindowsAppSDKSelfContained=true")) "package build must publish Windows App SDK self-contained applications"
Assert-True ($packageScript.Contains('$dotNetRuntimeVersion = "8.0.29"')) "package build must pin the reviewed .NET runtime patch"
Assert-True ($packageScript.Contains('FowanReleaseRuntimeVersion=$dotNetRuntimeVersion')) "package build must use the pinned .NET runtime patch"
Assert-True (-not $packageScript.Contains("windowsdesktop-runtime-8-x64.exe")) "package build must not stage the .NET runtime installer"
Assert-True (-not $packageScript.Contains("WindowsAppRuntimeInstall-x64.exe")) "package build must not stage the Windows App Runtime installer"
Assert-True ($packageScript.Contains("vc_redist.x64.exe")) "package build must stage the VC++ runtime"

$closeScriptPath = Join-Path $repositoryRoot "installer/windows/close-fowan-processes.ps1"
$closeTokens = $null
$closeErrors = $null
$closeAst = [Management.Automation.Language.Parser]::ParseFile($closeScriptPath, [ref]$closeTokens, [ref]$closeErrors)
Assert-True ($closeErrors.Count -eq 0) "installer process-close script must parse"
$allowedNamesAssignment = $closeAst.Find({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$allowedNames'
}, $false)
$processFilter = $closeAst.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-FowanInstallProcesses'
}, $false)
Assert-True ($null -ne $allowedNamesAssignment -and $null -ne $processFilter) "installer must expose its bounded process selection"
& {
    . ([scriptblock]::Create($allowedNamesAssignment.Extent.Text))
    $installRoot = Join-Path ([IO.Path]::GetTempPath()) 'Fowan installer process-selection fixture'
    $normalizedRoot = [IO.Path]::GetFullPath($installRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $fixtures = @(
        [pscustomobject]@{ ProcessId = 101; Name = 'Fowan.ToolCenter.Windows.exe'; ExecutablePath = (Join-Path $installRoot 'Tools/ToolCenter/Fowan.ToolCenter.Windows.exe') },
        [pscustomobject]@{ ProcessId = 102; Name = 'Fowan.Windows.exe'; ExecutablePath = (Join-Path $installRoot 'Fowan.Windows.exe') },
        [pscustomobject]@{ ProcessId = 201; Name = 'Fowan.ToolCenter.Windows.exe'; ExecutablePath = (Join-Path ($installRoot + '-other') 'Tools/ToolCenter/Fowan.ToolCenter.Windows.exe') },
        [pscustomobject]@{ ProcessId = 202; Name = 'Fowan.ToolCenter.Windows.Dev.exe'; ExecutablePath = (Join-Path $installRoot 'Tools/ToolCenter/Fowan.ToolCenter.Windows.Dev.exe') },
        [pscustomobject]@{ ProcessId = 203; Name = 'yym.exe'; ExecutablePath = (Join-Path $installRoot 'Tools/yym.exe') },
        [pscustomobject]@{ ProcessId = 204; Name = 'Fowan.ToolCenter.Windows.exe'; ExecutablePath = $null }
    )
    function Get-CimInstance { param([string]$ClassName) $fixtures }
    . ([scriptblock]::Create($processFilter.Extent.Text))
    $selected = @(Get-FowanInstallProcesses)
    Assert-True ($selected.Count -eq 2 -and 101 -in $selected.ProcessId -and 102 -in $selected.ProcessId) "upgrade must select the installed Tool Center and Toolbox while excluding other installs, development tools and yym"
}

Write-Host "Windows installer prerequisite regression checks passed."

#requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Assert-MicrosoftSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne "Valid" -or
        $signature.SignerCertificate.Subject -notlike "*Microsoft Corporation*") {
        throw "$Description signature validation failed: $Path"
    }
}

function Install-Prerequisite {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,
        [Parameter(Mandatory = $true)]
        [string]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [int[]]$SuccessExitCodes = @(0)
    )

    $path = Join-Path $PSScriptRoot $FileName
    Assert-MicrosoftSignature -Path $path -Description $Description
    Write-Host "Installing $Description..."
    $process = Start-Process -FilePath $path -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -notin $SuccessExitCodes) {
        throw "$Description installation failed with exit code $($process.ExitCode)."
    }
}

Install-Prerequisite -FileName "windowsdesktop-runtime-8-x64.exe" -Arguments "/install /quiet /norestart" -Description ".NET 8 Desktop Runtime x64" -SuccessExitCodes @(0, 3010)
Install-Prerequisite -FileName "WindowsAppRuntimeInstall-x64.exe" -Arguments "--quiet" -Description "Windows App Runtime 2.2 x64"
Install-Prerequisite -FileName "vc_redist.x64.exe" -Arguments "/install /quiet /norestart" -Description "Microsoft Visual C++ Redistributable x64" -SuccessExitCodes @(0, 3010)

Write-Host "Fowan shared runtime prerequisites are installed."

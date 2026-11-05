param()

$ErrorActionPreference = "Stop"
$scriptsRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $scriptsRoot "core-artifact-compatibility.ps1")

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern, [string]$Message) {
    $failure = $null
    try {
        & $Action
    }
    catch {
        $failure = $_.Exception.Message
    }
    Assert-True ($null -ne $failure -and $failure -match $Pattern) $Message
}

$repositoryRoot = Split-Path -Parent $scriptsRoot
$contract = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repositoryRoot "protocol/ai/v0.1/contract.json") | ConvertFrom-Json
$artifact = "C:\fixture\fowan-core.exe"

function New-HandshakeResponse(
    [int]$ContractRevision = 1,
    [object[]]$Capabilities = @($contract.capabilities)
) {
    [ordered]@{
        jsonrpc = "2.0"
        id = 1
        result = [ordered]@{
            engineVersion = "0.1.0"
            protocolVersion = $contract.protocolVersion
            contractRevision = $ContractRevision
            capabilities = $Capabilities
        }
    } | ConvertTo-Json -Depth 5 -Compress
}

Assert-FowanCoreHandshakeResponse `
    -ResponseJson (New-HandshakeResponse) `
    -Contract $contract `
    -ArtifactPath $artifact

$protocolMismatch = [ordered]@{
    jsonrpc = "2.0"
    id = 1
    error = [ordered]@{
        code = "protocol_mismatch"
        message = "Fowan Core protocol version is not supported"
    }
} | ConvertTo-Json -Depth 5 -Compress
Assert-Throws {
    Assert-FowanCoreHandshakeResponse `
        -ResponseJson $protocolMismatch `
        -Contract $contract `
        -ArtifactPath $artifact
} "protocol_mismatch" "legacy Core protocol mismatch must be rejected before the runnable tree changes"

Assert-Throws {
    Assert-FowanCoreHandshakeResponse `
        -ResponseJson (New-HandshakeResponse -ContractRevision 2) `
        -Contract $contract `
        -ArtifactPath $artifact
} "revision 1" "a Core artifact with a different contract revision must be rejected"

$missingReport = @($contract.capabilities | Where-Object { $_ -ne "ai.report.v1" })
Assert-Throws {
    Assert-FowanCoreHandshakeResponse `
        -ResponseJson (New-HandshakeResponse -Capabilities $missingReport) `
        -Contract $contract `
        -ArtifactPath $artifact
} "missing ai.report.v1" "a Core artifact missing the Report capability must be rejected"

$unexpectedCapability = @($contract.capabilities) + "ai.future.v1"
Assert-Throws {
    Assert-FowanCoreHandshakeResponse `
        -ResponseJson (New-HandshakeResponse -Capabilities $unexpectedCapability) `
        -Contract $contract `
        -ArtifactPath $artifact
} "unexpected ai.future.v1" "a Core artifact with an unknown capability must be rejected like the client validator"

$buildScript = Get-Content -Raw -LiteralPath (Join-Path $scriptsRoot "build-windows.ps1")
Assert-True (
    $buildScript.Contains("Test-FowanCoreArtifactCompatibility")) (
    "the unified Windows build must validate Core before building or replacing the runnable tree")

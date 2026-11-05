param()

$ErrorActionPreference = 'Stop'
$scriptsRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $scriptsRoot 'verify-development-policy.ps1')

function Assert-Rejected([scriptblock]$action, [string]$expected) {
    try {
        & $action
    }
    catch {
        if ($_.Exception.Message -notlike "*$expected*") {
            throw "Expected rejection containing '$expected', got: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected governance fixture to be rejected: $expected"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "fowan-policy-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $headings = $requiredHeadings -join [Environment]::NewLine
    $draftPath = Join-Path $temp 'DP-9996-draft.md'
    Set-Content -LiteralPath $draftPath -Encoding utf8 -Value "id: DP-9996`nstatus: draft`ncomponents:`n  - architecture-tests`n`n$headings"
    $draft = Get-ProposalMetadata (Get-Item -LiteralPath $draftPath)
    $proposalsById[$draft.Id] = $draft
    Assert-Rejected { Resolve-Proposal $draft.Id 'draft fixture' } 'accepted or implemented'
    $proposalsById.Remove($draft.Id)

    $adrPath = Join-Path $temp '9998-fake.md'
    Set-Content -LiteralPath $adrPath -Encoding utf8 -Value "# ADR-9998: Fake decision`n`n## Status`n`nProposed"
    Assert-Rejected { Get-AdrMetadata (Get-Item -LiteralPath $adrPath) } 'Accepted status'

    Assert-Rejected { Resolve-Proposal 'DP-0001' 'mismatch fixture' 'missing-component' } 'does not cover component'
    Assert-Rejected { Assert-BaselineReference 'baseline' 'fake-component' @{} 'Component' } 'frozen baseline ledger'
    Assert-Rejected { Assert-DependencyEntry 'fake-dependency' ([pscustomobject]@{ proposal = 'DP-0001' }) } 'must document purpose'
    Assert-Rejected { Assert-CorrespondingAdrs @($baseline.governanceAdrs) @('ADR-0001') } 'not an unrelated ADR'
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

Write-Host 'Development policy negative fixtures passed.'

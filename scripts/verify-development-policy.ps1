param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "docs/component-manifest.json"
$dependencyPath = Join-Path $repoRoot "docs/dependency-manifest.json"
$baselinePath = Join-Path $repoRoot "docs/architecture-baseline.json"
$proposalRoot = Join-Path $repoRoot "docs/design-proposals"
$adrRoot = Join-Path $repoRoot "docs/adr"
$validStatuses = @("draft", "accepted", "implemented", "superseded")
$requiredHeadings = @(
    "# Problem and users",
    "## Goals and non-goals",
    "## Repository and component boundaries",
    "## Interfaces and data flow",
    "## Failure, cancellation, and atomicity",
    "## Compatibility, migration, and rollback",
    "## Security, privacy, dependencies, and permissions",
    "## Test and acceptance plan",
    "## Reuse and duplication analysis"
)

function Get-ProposalMetadata([IO.FileInfo]$file) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    if ($content -notmatch '(?m)^id:\s*(DP-\d{4})\s*$') {
        throw "Design proposal has no valid id: $($file.FullName)"
    }
    $id = $Matches[1]
    if ($content -notmatch '(?m)^status:\s*([a-z]+)\s*$' -or $Matches[1] -notin $validStatuses) {
        throw "Design proposal $id has an invalid status."
    }
    $status = $Matches[1]
    foreach ($heading in $requiredHeadings) {
        if (-not $content.Contains($heading, [StringComparison]::Ordinal)) {
            throw "Design proposal $id is missing required heading: $heading"
        }
    }
    $components = @()
    if ($content -match '(?ms)^components:\s*\r?\n(?<items>(?:\s+-\s+[^\r\n]+\r?\n)+)') {
        $components = @([regex]::Matches($Matches['items'], '(?m)^\s+-\s+(?<id>[a-z0-9][a-z0-9.-]*)\s*$') |
            ForEach-Object { $_.Groups['id'].Value })
    }
    [pscustomobject]@{ Id = $id; Status = $status; Path = $file.FullName; Components = $components }
}

function Get-AdrMetadata([IO.FileInfo]$file) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    if ($content -notmatch '(?m)^#\s+ADR-(\d{4}):') {
        throw "ADR has no valid ADR-NNNN title: $($file.FullName)"
    }
    $id = "ADR-$($Matches[1])"
    if ($content -notmatch '(?im)(?:^##\s+Status\s*\r?\n+\s*Accepted\.?\s*$|^-\s*Status:\s*Accepted\s*$)') {
        throw "$id must declare Accepted status: $($file.FullName)"
    }
    [pscustomobject]@{ Id = $id; Path = $file.FullName }
}

$proposalsById = @{}
$proposalsByPath = @{}
Get-ChildItem -LiteralPath $proposalRoot -File -Filter 'DP-*.md' | ForEach-Object {
    $metadata = Get-ProposalMetadata $_
    if ($proposalsById.ContainsKey($metadata.Id)) { throw "Duplicate design proposal id: $($metadata.Id)" }
    $relative = [IO.Path]::GetRelativePath($repoRoot, $metadata.Path).Replace('\', '/')
    $proposalsById[$metadata.Id] = $metadata
    $proposalsByPath[$relative] = $metadata
}

$adrsById = @{}
$adrsByPath = @{}
if (Test-Path -LiteralPath $adrRoot) {
    Get-ChildItem -LiteralPath $adrRoot -File -Filter '*.md' | ForEach-Object {
        $metadata = Get-AdrMetadata $_
        if ($adrsById.ContainsKey($metadata.Id)) { throw "Duplicate ADR id: $($metadata.Id)" }
        $relative = [IO.Path]::GetRelativePath($repoRoot, $metadata.Path).Replace('\', '/')
        $adrsById[$metadata.Id] = $metadata
        $adrsByPath[$relative] = $metadata
    }
}

function Resolve-Proposal([string]$reference, [string]$owner, [string]$componentId = "") {
    if ([string]::IsNullOrWhiteSpace($reference) -or $reference -eq "baseline") {
        throw "$owner must resolve baseline through the frozen baseline ledger or reference a proposal."
    }
    $normalized = $reference.Replace('\', '/')
    $proposal = if ($proposalsByPath.ContainsKey($normalized)) { $proposalsByPath[$normalized] }
        elseif ($proposalsById.ContainsKey($reference)) { $proposalsById[$reference] }
        else { $null }
    if ($null -eq $proposal) { throw "$owner references a missing design proposal: $reference" }
    if ($proposal.Status -notin @('accepted', 'implemented')) {
        throw "$owner must reference an accepted or implemented proposal: $reference"
    }
    if ($componentId -and $componentId -notin $proposal.Components) {
        throw "$owner references $($proposal.Id), but that proposal does not cover component $componentId."
    }
    return $proposal
}

function Resolve-Adr([string]$reference, [string]$owner) {
    $normalized = $reference.Replace('\', '/')
    $adr = if ($adrsByPath.ContainsKey($normalized)) { $adrsByPath[$normalized] }
        elseif ($adrsById.ContainsKey($reference)) { $adrsById[$reference] }
        else { $null }
    if ($null -eq $adr) { throw "$owner references a missing or invalid ADR: $reference" }
    return $adr
}

function Assert-BaselineReference([string]$reference, [string]$key, [hashtable]$ledger, [string]$kind) {
    if ($reference -eq 'baseline' -and -not $ledger.ContainsKey($key)) {
        throw "$kind $key is not in the frozen baseline ledger."
    }
}

function Assert-DependencyEntry([string]$name, $entry) {
    foreach ($field in @('proposal', 'purpose', 'license', 'securityImpact', 'alternatives', 'removalStrategy')) {
        if (-not $entry.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$entry.$field)) {
            throw "Dependency $name must document $field."
        }
    }
}

function Assert-CorrespondingAdrs([object[]]$requiredReferences, [object[]]$resolvedIds) {
    foreach ($requiredReference in @($requiredReferences | Where-Object { $_ } | Select-Object -Unique)) {
        $requiredAdr = Resolve-Adr ([string]$requiredReference) 'Risk component'
        if ($requiredAdr.Id -notin $resolvedIds) {
            throw "High-risk changes require the corresponding $($requiredAdr.Id), not an unrelated ADR."
        }
    }
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$dependencies = Get-Content -Raw -LiteralPath $dependencyPath | ConvertFrom-Json
$baseline = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2 -or $dependencies.schemaVersion -ne 2 -or
    $baseline.schemaVersion -ne 1 -or $manifest.repository -ne 'Fowan' -or $baseline.repository -ne 'Fowan') {
    throw "Governance manifests have an unsupported schema or repository."
}

$englishPolicy = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "CONTRIBUTING.md")
$chinesePolicy = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "docs/development_guide.zh-CN.md")
if ($englishPolicy -notmatch '(?m)^Policy version:\s*(\d{4}-\d{2}-\d{2})\s*$') {
    throw "CONTRIBUTING.md must declare Policy version."
}
$policyVersion = $Matches[1]
if ($chinesePolicy -notmatch '(?m)^规范版本：\s*(\d{4}-\d{2}-\d{2})\s*$' -or
    $Matches[1] -ne $policyVersion -or $manifest.policyVersion -ne $policyVersion -or
    $dependencies.policyVersion -ne $policyVersion -or $baseline.policyVersion -ne $policyVersion) {
    throw "English policy, Chinese guide, and governance manifest versions must match."
}
[void](Resolve-Proposal ([string]$baseline.governanceProposal) 'Frozen baseline ledger')
foreach ($adr in @($baseline.governanceAdrs)) { [void](Resolve-Adr ([string]$adr) 'Frozen baseline ledger') }

$baselineComponents = @{}; @($baseline.components) | ForEach-Object { $baselineComponents[[string]$_] = $true }
$baselineModules = @{}; @($baseline.modules) | ForEach-Object { $baselineModules[[string]$_] = $true }
$baselineDependencies = @{}; @($baseline.dependencies) | ForEach-Object { $baselineDependencies[[string]$_] = $true }

$componentIds = @{}
$componentPaths = @{}
foreach ($component in $manifest.components) {
    $id = [string]$component.id
    if ([string]::IsNullOrWhiteSpace($id) -or $componentIds.ContainsKey($id)) {
        throw "Component ids must be non-empty and unique: $id"
    }
    $componentIds[$id] = $component
    $relative = ([string]$component.path).Replace('\', '/').TrimEnd('/')
    if ($componentPaths.ContainsKey($relative)) { throw "Duplicate component path: $relative" }
    $componentPaths[$relative] = $component
    $fullPath = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) { throw "Component path does not exist: $relative" }

    $proposal = [string]$component.proposal
    if ($proposal -eq 'baseline') {
        Assert-BaselineReference $proposal $id $baselineComponents 'Component'
    } else {
        [void](Resolve-Proposal $proposal "Component $id" $id)
    }
    if ($component.stateOwner -and ([string]$component.stateOwner -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$')) {
        throw "Component $id must name one concrete state-owner type, not an expression."
    }
    if ($component.kind -eq 'tool' -and [string]::IsNullOrWhiteSpace([string]$component.stateOwner)) {
        throw "Tool component $id must declare exactly one application state owner."
    }
    foreach ($adr in @($component.adrs) | Where-Object { $_ }) { [void](Resolve-Adr ([string]$adr) "Component $id") }
    foreach ($testPath in @($component.tests) | Where-Object { $_ }) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $testPath))) {
            throw "Component $id references a missing test path: $testPath"
        }
    }

    $registeredModules = @{}
    if ($component.modules) {
        foreach ($property in $component.modules.PSObject.Properties) {
            $registeredModules[$property.Name] = $true
            $moduleKey = "$id/$($property.Name)"
            if ([string]$property.Value -eq 'baseline') {
                Assert-BaselineReference ([string]$property.Value) $moduleKey $baselineModules 'Module'
            } else {
                [void](Resolve-Proposal ([string]$property.Value) "Module $moduleKey" $id)
            }
        }
    }
    Get-ChildItem -LiteralPath $fullPath -Directory | Where-Object { $_.Name -notin @('bin', 'obj', 'build', 'publish') } | ForEach-Object {
        if (-not $registeredModules.ContainsKey($_.Name)) { throw "Unregistered top-level module: $relative/$($_.Name)" }
    }
}

foreach ($baselineId in $baselineComponents.Keys) {
    if (-not $componentIds.ContainsKey($baselineId) -or [string]$componentIds[$baselineId].proposal -ne 'baseline') {
        throw "Frozen baseline component is missing or no longer marked baseline: $baselineId"
    }
}
foreach ($baselineModule in $baselineModules.Keys) {
    $parts = $baselineModule.Split('/', 2)
    if (-not $componentIds.ContainsKey($parts[0]) -or -not $componentIds[$parts[0]].modules.PSObject.Properties[$parts[1]] -or
        [string]$componentIds[$parts[0]].modules.PSObject.Properties[$parts[1]].Value -ne 'baseline') {
        throw "Frozen baseline module is missing or no longer marked baseline: $baselineModule"
    }
}

Get-ChildItem -LiteralPath (Join-Path $repoRoot 'apps/windows') -Recurse -File -Filter *.csproj | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($repoRoot, $_.DirectoryName).Replace('\', '/')
    if (-not ($componentPaths.Keys | Where-Object { $relative -eq $_ -or $relative.StartsWith("$_/", [StringComparison]::Ordinal) })) {
        throw "Project is not registered in the component manifest: $relative"
    }
}

$registeredDependencies = @{}
foreach ($property in $dependencies.dependencies.PSObject.Properties) {
    $name = $property.Name
    $entry = $property.Value
    $registeredDependencies[$name] = $true
    Assert-DependencyEntry $name $entry
    if ([string]$entry.proposal -eq 'baseline') {
        Assert-BaselineReference ([string]$entry.proposal) $name $baselineDependencies 'Dependency'
    } else {
        [void](Resolve-Proposal ([string]$entry.proposal) "Dependency $name")
    }
}
foreach ($baselineDependency in $baselineDependencies.Keys) {
    if (-not $registeredDependencies.ContainsKey($baselineDependency) -or
        [string]$dependencies.dependencies.$baselineDependency.proposal -ne 'baseline') {
        throw "Frozen baseline dependency is missing or no longer marked baseline: $baselineDependency"
    }
}
Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter *.csproj | Where-Object {
    $_.FullName -notmatch '[\\/](?:build|publish|obj|bin)[\\/]'
} | ForEach-Object {
    [xml]$project = Get-Content -Raw -LiteralPath $_.FullName
    foreach ($reference in @($project.Project.ItemGroup.PackageReference)) {
        $name = [string]$reference.Include
        if ($name -and -not $registeredDependencies.ContainsKey($name)) {
            throw "Unregistered NuGet dependency $name in $($_.FullName)"
        }
    }
}

function Get-PrField([string]$body, [string]$name) {
    $match = [regex]::Match($body, "(?im)^-\s*$([regex]::Escape($name)):\s*(?<value>\S.+)$")
    if (-not $match.Success) { throw "Pull request body must complete: $name" }
    return $match.Groups['value'].Value.Trim()
}

if ($env:GITHUB_EVENT_NAME -eq 'pull_request' -and (Test-Path -LiteralPath $env:GITHUB_EVENT_PATH)) {
    $event = Get-Content -Raw -LiteralPath $env:GITHUB_EVENT_PATH | ConvertFrom-Json
    $body = [string]$event.pull_request.body
    $proposalField = Get-PrField $body 'Proposal or baseline change'
    foreach ($field in @('Components changed', 'Application state owner', 'Dependency-direction changes',
        'User-visible behavior', 'Failure, cancellation, and cleanup behavior', 'Compatibility or migration impact',
        'Security, privacy, permissions, or dependency impact')) { [void](Get-PrField $body $field) }
    $adrField = Get-PrField $body 'ADRs (if required)'
    $base = [string]$event.pull_request.base.sha
    $changed = @(git -C $repoRoot diff --name-only "$base...HEAD" | ForEach-Object { $_.Replace('\', '/') })
    $changedComponents = @($manifest.components | Where-Object {
        $prefix = ([string]$_.path).Replace('\', '/').TrimEnd('/')
        $changed | Where-Object { $_ -eq $prefix -or $_.StartsWith("$prefix/", [StringComparison]::Ordinal) }
    })

    if ($proposalField -notmatch '(?i)^baseline change$') {
        $proposal = Resolve-Proposal $proposalField 'Pull request'
        foreach ($component in $changedComponents) {
            if ($component.id -notin $proposal.Components -and [string]$component.proposal -ne 'baseline') {
                throw "Pull request proposal $($proposal.Id) does not cover changed component $($component.id)."
            }
        }
    }
    foreach ($component in $changedComponents | Where-Object { [string]$_.proposal -ne 'baseline' }) {
        $requiredProposal = Resolve-Proposal ([string]$component.proposal) "Component $($component.id)" ([string]$component.id)
        if ($proposalField -notmatch [regex]::Escape($requiredProposal.Id) -and
            $proposalField -notmatch [regex]::Escape([IO.Path]::GetRelativePath($repoRoot, $requiredProposal.Path).Replace('\', '/'))) {
            throw "Changed non-baseline component $($component.id) requires $($requiredProposal.Id) in the PR proposal field."
        }
    }

    $riskChange = $changed | Where-Object {
        $_ -match '^(?:protocol/|docs/architecture-baseline\.json|docs/adr/)' -or
        $_ -match '(?:schema|migration|credential|security|protector|secret|dependency-manifest)'
    }
    if ($riskChange -or $changed -contains 'docs/architecture-baseline.json') {
        if ($adrField -match '^(?i:N/?A|none)$') { throw "High-risk or baseline-ledger changes require a real ADR reference." }
        $references = @($adrField -split '[,; ]+' | Where-Object { $_ -match '^(?:ADR-\d{4}|docs/adr/.+\.md)$' })
        if (-not $references) { throw "ADRs field does not contain a resolvable ADR reference." }
        $resolvedAdrIds = @($references | ForEach-Object { (Resolve-Adr $_ 'Pull request').Id })
        $requiredAdrReferences = @($baseline.governanceAdrs)
        foreach ($component in $changedComponents) {
            $prefix = ([string]$component.path).Replace('\', '/').TrimEnd('/')
            if ($riskChange | Where-Object { $_ -eq $prefix -or $_.StartsWith("$prefix/", [StringComparison]::Ordinal) }) {
                $requiredAdrReferences += @($component.adrs)
            }
        }
        Assert-CorrespondingAdrs $requiredAdrReferences $resolvedAdrIds
        if ($changed -contains 'docs/architecture-baseline.json' -and $proposalField -match '(?i)^baseline change$') {
            throw "The frozen baseline ledger can change only under an accepted proposal, never as a baseline change."
        }
    }
}

Write-Host "Development policy verification passed."

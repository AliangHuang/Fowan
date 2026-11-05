$ErrorActionPreference = "Stop"

function Write-FowanCoreProtocolFrame {
    param(
        [Parameter(Mandatory)]
        [IO.Stream]$Stream,
        [Parameter(Mandatory)]
        [string]$Json,
        [Parameter(Mandatory)]
        [int]$MaximumFrameBytes
    )

    $body = [Text.Encoding]::UTF8.GetBytes($Json)
    if ($body.Length -gt $MaximumFrameBytes) {
        throw "Fowan Core compatibility request exceeds the public protocol frame limit."
    }

    $header = [Text.Encoding]::ASCII.GetBytes("Content-Length: $($body.Length)`r`n`r`n")
    $Stream.Write($header, 0, $header.Length)
    $Stream.Write($body, 0, $body.Length)
    $Stream.Flush()
}

function Read-FowanCoreProtocolFrame {
    param(
        [Parameter(Mandatory)]
        [IO.Stream]$Stream,
        [Parameter(Mandatory)]
        [int]$MaximumHeaderBytes,
        [Parameter(Mandatory)]
        [int]$MaximumFrameBytes
    )

    $header = [Collections.Generic.List[byte]]::new()
    $one = [byte[]]::new(1)
    while ($true) {
        $read = $Stream.Read($one, 0, 1)
        if ($read -eq 0) {
            throw "Fowan Core closed the compatibility connection before returning a response."
        }

        $header.Add($one[0])
        if ($header.Count -gt $MaximumHeaderBytes) {
            throw "Fowan Core returned a protocol header that exceeds the public limit."
        }

        $count = $header.Count
        if ($count -ge 4 -and
            $header[$count - 4] -eq 13 -and
            $header[$count - 3] -eq 10 -and
            $header[$count - 2] -eq 13 -and
            $header[$count - 1] -eq 10) {
            break
        }
    }

    $headerText = [Text.Encoding]::ASCII.GetString($header.ToArray())
    $headerLines = @($headerText.Split(
        [string[]]@("`r`n"),
        [StringSplitOptions]::RemoveEmptyEntries))
    if ($headerLines.Count -ne 1 -or
        -not $headerLines[0].StartsWith("Content-Length:", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fowan Core returned an invalid compatibility response header."
    }

    $length = 0
    if (-not [int]::TryParse(
            $headerLines[0].Substring("Content-Length:".Length).Trim(),
            [ref]$length) -or
        $length -lt 0 -or
        $length -gt $MaximumFrameBytes) {
        throw "Fowan Core returned an invalid compatibility response length."
    }

    $body = [byte[]]::new($length)
    $offset = 0
    while ($offset -lt $length) {
        $read = $Stream.Read($body, $offset, $length - $offset)
        if ($read -eq 0) {
            throw "Fowan Core closed the compatibility connection before completing its response."
        }
        $offset += $read
    }

    [Text.Encoding]::UTF8.GetString($body)
}

function Assert-FowanCoreHandshakeResponse {
    param(
        [Parameter(Mandatory)]
        [string]$ResponseJson,
        [Parameter(Mandatory)]
        [pscustomobject]$Contract,
        [Parameter(Mandatory)]
        [string]$ArtifactPath
    )

    try {
        $response = $ResponseJson | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Fowan Core artifact returned invalid JSON during the compatibility handshake: $ArtifactPath"
    }

    $responseProperties = @($response.PSObject.Properties.Name)
    if ($response.jsonrpc -ne "2.0" -or $response.id -ne 1) {
        throw "Fowan Core artifact returned an invalid compatibility response envelope: $ArtifactPath"
    }

    if ($responseProperties -contains "error") {
        $code = if ($response.error.code -is [string]) { $response.error.code } else { "unknown_error" }
        throw "Fowan Core artifact is incompatible with the public AI protocol ($code): $ArtifactPath"
    }

    if ($responseProperties.Count -ne 3 -or
        $responseProperties -notcontains "result" -or
        $null -eq $response.result) {
        throw "Fowan Core artifact returned an invalid compatibility response: $ArtifactPath"
    }

    $result = $response.result
    $resultProperties = @($result.PSObject.Properties.Name)
    if ($resultProperties.Count -ne 4 -or
        $resultProperties -notcontains "engineVersion" -or
        $resultProperties -notcontains "protocolVersion" -or
        $resultProperties -notcontains "contractRevision" -or
        $resultProperties -notcontains "capabilities" -or
        -not ($result.engineVersion -is [string]) -or
        [string]::IsNullOrWhiteSpace($result.engineVersion) -or
        $result.protocolVersion -ne $Contract.protocolVersion -or
        $result.contractRevision -ne $Contract.contractRevision) {
        throw "Fowan Core artifact does not match public AI protocol $($Contract.protocolVersion) revision $($Contract.contractRevision): $ArtifactPath"
    }

    $actualCapabilities = @($result.capabilities)
    $expectedCapabilities = @($Contract.capabilities)
    $invalidCapabilities = @($actualCapabilities | Where-Object {
        -not ($_ -is [string]) -or [string]::IsNullOrWhiteSpace($_)
    })
    if ($invalidCapabilities.Count -gt 0 -or
        @($actualCapabilities | Select-Object -Unique).Count -ne $actualCapabilities.Count) {
        throw "Fowan Core artifact returned an invalid capability list: $ArtifactPath"
    }

    $unexpected = @($actualCapabilities | Where-Object { $_ -notin $expectedCapabilities })
    $missing = @($expectedCapabilities | Where-Object { $_ -notin $actualCapabilities })
    if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        $details = @()
        if ($missing.Count -gt 0) { $details += "missing $($missing -join ', ')" }
        if ($unexpected.Count -gt 0) { $details += "unexpected $($unexpected -join ', ')" }
        throw "Fowan Core artifact capabilities are incompatible ($($details -join '; ')): $ArtifactPath"
    }
}

function Test-FowanCoreArtifactCompatibility {
    param(
        [Parameter(Mandatory)]
        [string]$ArtifactPath,
        [Parameter(Mandatory)]
        [string]$ContractPath
    )

    if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
        throw "Fowan Core artifact was not found: $ArtifactPath"
    }
    if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
        throw "Public AI protocol contract was not found: $ContractPath"
    }

    $contract = Get-Content -Raw -Encoding UTF8 -LiteralPath $ContractPath |
        ConvertFrom-Json -ErrorAction Stop
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "Fowan core compatibility $([guid]::NewGuid().ToString('N'))"
    $process = $null
    $pipe = $null
    try {
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = [IO.Path]::GetFullPath($ArtifactPath)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $startInfo.EnvironmentVariables["LOCALAPPDATA"] = $temporaryRoot
        $process = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) {
            throw "Fowan Core compatibility process could not be started."
        }

        $tokenPath = Join-Path $temporaryRoot "Fowan/Core/pipe-token"
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $tokenPath -PathType Leaf)) {
            $process.Refresh()
            if ($process.HasExited) {
                throw "Fowan Core compatibility process exited with code $($process.ExitCode)."
            }
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Fowan Core compatibility process did not create its isolated endpoint."
            }
            Start-Sleep -Milliseconds 50
        }

        $token = (Get-Content -Raw -LiteralPath $tokenPath).Trim()
        if ($token -notmatch '^[a-f0-9]{32}$') {
            throw "Fowan Core compatibility process created an invalid isolated endpoint."
        }

        $pipe = [IO.Pipes.NamedPipeClientStream]::new(
            ".",
            "fowan-core-v1-$token",
            [IO.Pipes.PipeDirection]::InOut,
            [IO.Pipes.PipeOptions]::None)
        $pipe.Connect(5000)

        $request = [ordered]@{
            jsonrpc = "2.0"
            id = 1
            method = "engine.handshake"
            params = [ordered]@{
                protocolVersion = $contract.protocolVersion
                contractRevision = $contract.contractRevision
                requiredCapabilities = @($contract.capabilities)
            }
        } | ConvertTo-Json -Depth 5 -Compress
        Write-FowanCoreProtocolFrame `
            -Stream $pipe `
            -Json $request `
            -MaximumFrameBytes $contract.limits.maximumFrameBytes
        $response = Read-FowanCoreProtocolFrame `
            -Stream $pipe `
            -MaximumHeaderBytes $contract.limits.maximumHeaderBytes `
            -MaximumFrameBytes $contract.limits.maximumFrameBytes
        Assert-FowanCoreHandshakeResponse `
            -ResponseJson $response `
            -Contract $contract `
            -ArtifactPath $ArtifactPath
        Write-Host "Fowan Core compatibility: protocol $($contract.protocolVersion), revision $($contract.contractRevision), capabilities verified"
    }
    finally {
        if ($null -ne $pipe) {
            $pipe.Dispose()
        }
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                $process.Kill()
                $process.WaitForExit()
            }
            $process.Dispose()
        }
        if (Test-Path -LiteralPath $temporaryRoot) {
            $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
            $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') +
                [IO.Path]::DirectorySeparatorChar
            if (-not $resolvedTemporaryRoot.StartsWith(
                    $resolvedSystemTemp,
                    [StringComparison]::OrdinalIgnoreCase) -or
                (Split-Path -Leaf $resolvedTemporaryRoot) -notlike "Fowan core compatibility *") {
                throw "Refusing to clean an unexpected Core compatibility directory: $resolvedTemporaryRoot"
            }
            [IO.Directory]::Delete($resolvedTemporaryRoot, $true)
        }
    }
}

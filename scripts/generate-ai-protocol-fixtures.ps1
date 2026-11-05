param(
    [string]$ProtocolRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProtocolRoot)) {
    $ProtocolRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "protocol/ai/v0.1"
}
$ProtocolRoot = [IO.Path]::GetFullPath($ProtocolRoot)

function Reset-GeneratedDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $expectedPrefix = $ProtocolRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a generated directory outside the protocol root: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath | Out-Null
}

function Write-Json([string]$Path, $Value) {
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
    $json = ConvertTo-Json -InputObject $Value -Depth 40
    $normalizedJson = $json.Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText($Path, $normalizedJson + "`n", [Text.UTF8Encoding]::new($false))
}

function Operation([string]$Name, $Params, [string]$ResultDefinition, $Result) {
    [ordered]@{ Name = $Name; Params = $Params; ResultDefinition = $ResultDefinition; Result = $Result }
}

$sharedSchema = Get-Content -Raw -LiteralPath (Join-Path $ProtocolRoot "schema/methods.schema.json") | ConvertFrom-Json -AsHashtable
$sharedDefinitions = $sharedSchema['$defs']

function Get-DefinitionClosure([string[]]$Names) {
    $result = [ordered]@{}
    $pending = [Collections.Generic.Queue[string]]::new()
    foreach ($name in $Names) { $pending.Enqueue($name) }
    while ($pending.Count -gt 0) {
        $name = $pending.Dequeue()
        if ($result.Contains($name)) { continue }
        if (-not $sharedDefinitions.Contains($name)) { throw "Unknown shared schema definition: $name" }
        $definition = $sharedDefinitions[$name]
        $result[$name] = $definition
        $json = ConvertTo-Json -InputObject $definition -Depth 40 -Compress
        foreach ($match in [regex]::Matches($json, '#/\\u0024defs/(?<name>[^"/]+)|#/\$defs/(?<name>[^"/]+)')) {
            $pending.Enqueue($match.Groups['name'].Value)
        }
    }
    return $result
}

$timestamp = "2026-01-01T00:00:00Z"
$channel = [ordered]@{ id = "openai"; kind = "openai_compatible"; displayName = "OpenAI"; defaultBaseUrl = "https://api.openai.com/v1"; builtIn = $true; enabled = $true }
$credential = [ordered]@{ id = "credential-1"; channelId = "openai"; label = "Primary"; baseUrl = "https://api.openai.com/v1"; secretHint = "••••1234"; enabled = $true; lastTestStatus = $null; lastTestAt = $null; createdAt = $timestamp; updatedAt = $timestamp }
$model = [ordered]@{ id = "model-1"; credentialId = "credential-1"; modelId = "gpt-4.1-mini"; displayName = "GPT 4.1 mini"; source = "preset"; enabled = $true; thinkingEnabled = $true; thinkingEffortOptions = @("high", "max"); contextWindowTokens = 1048576; maxOutputTokens = 32768; limitsConfigured = $true; lastTestStatus = $null; lastTestAt = $null; createdAt = $timestamp; updatedAt = $timestamp }
$binding = [ordered]@{ featureId = "ai.chat"; credentialId = "credential-1"; modelProfileId = "model-1"; thinkingEffort = "high"; updatedAt = $timestamp }
$summary = [ordered]@{ id = "conversation-1"; title = "Conversation"; createdAt = $timestamp; updatedAt = $timestamp }
$idResult = [ordered]@{ id = "generated-id" }
$deletedResult = [ordered]@{ deleted = $true }
$statusResult = [ordered]@{ status = "success" }
$invocationResult = [ordered]@{ invocationId = "invocation-1"; conversationId = "conversation-1"; assistantMessageId = "message-2" }
$contextEstimate = [ordered]@{ estimatedInputTokens = 256; safeInputTokens = 963379; contextWindowTokens = 1048576; maxOutputTokens = 32768; usageRatio = 0.000266; action = "ready"; compactedThroughMessageId = $null }
$messagesPage = [ordered]@{ conversationId = "conversation-1"; activeLeafMessageId = "message-2"; items = @(); nextCursor = $null; hasMore = $false; summary = $null }
$reportTask = [ordered]@{ title = "完成汇报工具设计"; notes = "已完成主界面概念图"; listName = "产品研发"; level = 1; important = $true; startDate = "2026-07-20"; dueDate = "2026-07-21"; completedAt = "2026-07-21T09:00:00+08:00"; status = "completed" }
$reportCell = [ordered]@{ value = "周报"; valueKind = "text"; editable = $true }
$reportDocument = [ordered]@{ format = "text"; blocks = @([ordered]@{ kind = "heading1"; text = "周报"; bold = $true; italic = $false; link = $null; isChecked = $false; table = $null }); sheets = @() }
$reportOutput = [ordered]@{ document = $reportDocument }

$operations = @(
    (Operation "engine.handshake" ([ordered]@{ protocolVersion = "0.1"; contractRevision = 1; requiredCapabilities = @("ai.config.v1", "ai.chat.v1", "ai.chat.context.v1", "ai.chat.branching.v1", "ai.report.v1") }) "handshakeResult" ([ordered]@{ engineVersion = "0.1.0"; protocolVersion = "0.1"; contractRevision = 1; capabilities = @("ai.config.v1", "ai.chat.v1", "ai.chat.context.v1", "ai.chat.branching.v1", "ai.report.v1") })),
    (Operation "ai.channels.list" ([ordered]@{}) "channelsResult" @($channel)),
    (Operation "ai.channels.create" ([ordered]@{ displayName = "Custom"; defaultBaseUrl = "https://api.example.com/v1"; enabled = $true }) "idResult" $idResult),
    (Operation "ai.channels.update" ([ordered]@{ id = "channel-1"; displayName = "Custom"; defaultBaseUrl = "https://api.example.com/v1"; enabled = $true }) "idResult" $idResult),
    (Operation "ai.channels.delete" ([ordered]@{ id = "channel-1" }) "deletedResult" $deletedResult),
    (Operation "ai.credentials.list" ([ordered]@{}) "credentialsResult" @($credential)),
    (Operation "ai.credentials.upsert" ([ordered]@{ channelId = "openai"; label = "Primary"; baseUrl = "https://api.openai.com/v1"; secret = "example-secret"; enabled = $true; initialModelIds = @("gpt-4.1-mini"); thinkingEnabled = $true }) "idResult" $idResult),
    (Operation "ai.credentials.delete" ([ordered]@{ id = "credential-1" }) "deletedResult" $deletedResult),
    (Operation "ai.credentials.test" ([ordered]@{ credentialId = "credential-1" }) "statusResult" $statusResult),
    (Operation "ai.models.list" ([ordered]@{}) "modelsResult" @($model)),
    (Operation "ai.models.upsert" ([ordered]@{ credentialId = "credential-1"; modelId = "gpt-4.1-mini"; displayName = "GPT 4.1 mini"; source = "preset"; enabled = $true; contextWindowTokens = 1048576; maxOutputTokens = 32768; thinkingEnabled = $true }) "idResult" $idResult),
    (Operation "ai.models.delete" ([ordered]@{ id = "model-1" }) "deletedResult" $deletedResult),
    (Operation "ai.models.test" ([ordered]@{ modelProfileId = "model-1" }) "statusResult" $statusResult),
    (Operation "ai.models.presets" ([ordered]@{}) "presetsResult" @([ordered]@{ channelId = "openai"; modelId = "gpt-4.1-mini"; displayName = "GPT 4.1 mini"; contextWindowTokens = 1048576; maxOutputTokens = 32768 })),
    (Operation "ai.toolFeatures.list" ([ordered]@{}) "toolFeaturesResult" @([ordered]@{ featureId = "ai.chat"; toolId = "ai-chat"; displayName = "AI Chat"; requiredCapabilities = @("ai.chat.v1") }, [ordered]@{ featureId = "ai.report"; toolId = "report"; displayName = "Report"; requiredCapabilities = @("ai.report.v1") })),
    (Operation "ai.bindings.list" ([ordered]@{}) "bindingsResult" @($binding)),
    (Operation "ai.bindings.upsert" ([ordered]@{ featureId = "ai.chat"; modelProfileId = "model-1"; thinkingEffort = "high" }) "binding" $binding),
    (Operation "ai.bindings.delete" ([ordered]@{ featureId = "ai.chat" }) "deletedResult" $deletedResult),
    (Operation "ai.consents.check" ([ordered]@{ endpoint = "https://api.openai.com/v1" }) "consentResult" ([ordered]@{ granted = $false; endpoint = "https://api.openai.com/v1" })),
    (Operation "ai.consents.grant" ([ordered]@{ endpoint = "https://api.openai.com/v1" }) "consentResult" ([ordered]@{ granted = $true; endpoint = "https://api.openai.com/v1" })),
    (Operation "ai.conversations.list" ([ordered]@{}) "conversationsResult" @($summary)),
    (Operation "ai.conversations.create" ([ordered]@{ title = "Conversation" }) "conversationSummary" $summary),
    (Operation "ai.conversations.get" ([ordered]@{ id = "conversation-1" }) "conversationDetailResult" ([ordered]@{ id = "conversation-1"; title = "Conversation"; createdAt = $timestamp; updatedAt = $timestamp; messages = @() })),
    (Operation "ai.conversations.rename" ([ordered]@{ id = "conversation-1"; title = "Renamed" }) "renamedResult" ([ordered]@{ renamed = $true })),
    (Operation "ai.conversations.delete" ([ordered]@{ id = "conversation-1" }) "deletedResult" $deletedResult),
    (Operation "ai.conversations.messages.list" ([ordered]@{ conversationId = "conversation-1"; branchLeafMessageId = "message-2"; cursor = $null; limit = 50 }) "messagesPageResult" $messagesPage),
    (Operation "ai.conversations.branch.select" ([ordered]@{ conversationId = "conversation-1"; leafMessageId = "message-2" }) "branchSelectResult" ([ordered]@{ conversationId = "conversation-1"; activeLeafMessageId = "message-2" })),
    (Operation "ai.chat.context.estimate" ([ordered]@{ conversationId = "conversation-1"; modelProfileId = "model-1"; branchLeafMessageId = "message-2"; draft = "Hello" }) "contextEstimateResult" $contextEstimate),
    (Operation "ai.chat.context.compact" ([ordered]@{ conversationId = "conversation-1"; credentialId = "credential-1"; modelProfileId = "model-1"; branchLeafMessageId = "message-2" }) "compactInvocationResult" ([ordered]@{ invocationId = "invocation-2"; conversationId = "conversation-1" })),
    (Operation "ai.chat.send" ([ordered]@{ conversationId = "conversation-1"; parentMessageId = "message-2"; credentialId = "credential-1"; modelProfileId = "model-1"; text = "Hello" }) "invocationResult" $invocationResult),
    (Operation "ai.chat.cancel" ([ordered]@{ invocationId = "invocation-1" }) "cancelledResult" ([ordered]@{ cancelled = $true })),
    (Operation "ai.chat.regenerate" ([ordered]@{ conversationId = "conversation-1"; credentialId = "credential-1"; modelProfileId = "model-1"; userMessageId = "message-1" }) "invocationResult" $invocationResult),
    (Operation "ai.report.generate" ([ordered]@{ reportType = "weekly"; rangeStart = "2026-07-20"; rangeEnd = "2026-07-26"; style = "professional"; customRequirements = "突出完成事项"; templateMode = "text"; template = $reportDocument; example = $null; attempt = 1; candidate = $null; validationFeedback = $null; completedTasks = @($reportTask); unfinishedTasks = @() }) "reportInvocationResult" ([ordered]@{ invocationId = "report-invocation-1" })),
    (Operation "ai.report.cancel" ([ordered]@{ invocationId = "report-invocation-1" }) "cancelledResult" ([ordered]@{ cancelled = $true }))
)

$parameterDefinitions = [ordered]@{
    "engine.handshake" = "handshakeParams"
    "ai.channels.list" = "empty"; "ai.channels.create" = "channelCreateParams"; "ai.channels.update" = "channelUpdateParams"; "ai.channels.delete" = "idParams"
    "ai.credentials.list" = "empty"; "ai.credentials.upsert" = "credentialUpsertParams"; "ai.credentials.delete" = "idParams"; "ai.credentials.test" = "credentialTestParams"
    "ai.models.list" = "empty"; "ai.models.upsert" = "modelUpsertParams"; "ai.models.delete" = "idParams"; "ai.models.test" = "modelTestParams"; "ai.models.presets" = "empty"
    "ai.toolFeatures.list" = "empty"; "ai.bindings.list" = "empty"; "ai.bindings.upsert" = "bindingUpsertParams"; "ai.bindings.delete" = "featureIdParams"
    "ai.consents.check" = "endpointParams"; "ai.consents.grant" = "endpointParams"
    "ai.conversations.list" = "empty"; "ai.conversations.create" = "conversationCreateParams"; "ai.conversations.get" = "idParams"; "ai.conversations.rename" = "conversationRenameParams"; "ai.conversations.delete" = "idParams"
    "ai.conversations.messages.list" = "messagesListParams"; "ai.conversations.branch.select" = "branchSelectParams"
    "ai.chat.context.estimate" = "contextEstimateParams"; "ai.chat.context.compact" = "contextCompactParams"
    "ai.chat.send" = "chatSendParams"; "ai.chat.cancel" = "chatCancelParams"; "ai.chat.regenerate" = "chatRegenerateParams"
    "ai.report.generate" = "reportGenerateParams"; "ai.report.cancel" = "chatCancelParams"
}

$notifications = @(
    [ordered]@{ Name = "ai.chat.started"; Params = $invocationResult },
    [ordered]@{ Name = "ai.chat.delta"; Params = [ordered]@{ invocationId = "invocation-1"; delta = "Hello" } },
    [ordered]@{ Name = "ai.chat.completed"; Params = [ordered]@{ invocationId = "invocation-1"; assistantMessageId = "message-2"; errorCode = $null; requestId = "req-safe-1" } },
    [ordered]@{ Name = "ai.chat.cancelled"; Params = [ordered]@{ invocationId = "invocation-1"; assistantMessageId = "message-2"; errorCode = $null; requestId = $null } },
    [ordered]@{ Name = "ai.chat.failed"; Params = [ordered]@{ invocationId = "invocation-1"; assistantMessageId = "message-2"; errorCode = "provider_unavailable"; requestId = "req-safe-1" } },
    [ordered]@{ Name = "ai.chat.context.compact.started"; Params = [ordered]@{ invocationId = "invocation-2"; conversationId = "conversation-1" } },
    [ordered]@{ Name = "ai.chat.context.compact.completed"; Params = [ordered]@{ invocationId = "invocation-2"; conversationId = "conversation-1"; summaryId = "summary-1"; throughMessageId = "message-20" } },
    [ordered]@{ Name = "ai.chat.context.compact.failed"; Params = [ordered]@{ invocationId = "invocation-2"; conversationId = "conversation-1"; errorCode = "provider_unavailable"; requestId = "req-safe-1" } },
    [ordered]@{ Name = "ai.report.started"; Params = [ordered]@{ invocationId = "report-invocation-1" } },
    [ordered]@{ Name = "ai.report.completed"; Params = [ordered]@{ invocationId = "report-invocation-1"; output = $reportOutput } },
    [ordered]@{ Name = "ai.report.cancelled"; Params = [ordered]@{ invocationId = "report-invocation-1"; errorCode = $null; requestId = $null } },
    [ordered]@{ Name = "ai.report.failed"; Params = [ordered]@{ invocationId = "report-invocation-1"; errorCode = "provider_unavailable"; requestId = "req-safe-1" } }
)

$errors = @(
    "invalid_argument", "not_found", "conflict", "protocol_mismatch", "handshake_required",
    "consent_required", "secret_store_unavailable", "secure_state_inconsistent",
    "protected_data_unavailable", "storage_unavailable", "provider_auth_failed",
    "provider_model_not_found", "provider_rate_limited", "provider_content_rejected",
    "provider_unavailable", "context_limit_exceeded", "context_compression_required",
    "message_too_large", "response_too_large", "timeout", "cancelled", "internal_error"
)

$schemaDirectory = Join-Path $ProtocolRoot "schema/generated"
$exampleDirectory = Join-Path $ProtocolRoot "examples/generated"
Reset-GeneratedDirectory $schemaDirectory
Reset-GeneratedDirectory $exampleDirectory
$vectors = [Collections.Generic.List[object]]::new()
$requestId = 1

foreach ($operation in $operations) {
    $slug = $operation.Name.Replace('.', '-')
    $requestSchemaRelative = "schema/generated/request-$slug.schema.json"
    $responseSchemaRelative = "schema/generated/response-$slug.schema.json"
    $requestPath = Join-Path $ProtocolRoot "examples/generated/request-$slug.json"
    $responsePath = Join-Path $ProtocolRoot "examples/generated/response-$slug.json"

    $parameterDefinition = $parameterDefinitions[$operation.Name]
    Write-Json (Join-Path $ProtocolRoot $requestSchemaRelative) ([ordered]@{
        '$schema' = "https://json-schema.org/draft/2020-12/schema"
        type = "object"; required = @("jsonrpc", "id", "method", "params"); additionalProperties = $false
        properties = [ordered]@{
            jsonrpc = [ordered]@{ const = "2.0" }
            id = [ordered]@{ '$ref' = "#/`$defs/id" }
            method = [ordered]@{ const = $operation.Name }
            params = [ordered]@{ '$ref' = "#/`$defs/$parameterDefinition" }
        }
        '$defs' = Get-DefinitionClosure @("id", $parameterDefinition)
    })
    Write-Json (Join-Path $ProtocolRoot $responseSchemaRelative) ([ordered]@{
        '$schema' = "https://json-schema.org/draft/2020-12/schema"
        type = "object"; required = @("jsonrpc", "id", "result"); additionalProperties = $false
        properties = [ordered]@{
            jsonrpc = [ordered]@{ const = "2.0" }
            id = [ordered]@{ '$ref' = "#/`$defs/id" }
            result = [ordered]@{ '$ref' = "#/`$defs/$($operation.ResultDefinition)" }
        }
        '$defs' = Get-DefinitionClosure @("id", $operation.ResultDefinition)
    })
    Write-Json $requestPath ([ordered]@{ jsonrpc = "2.0"; id = $requestId; method = $operation.Name; params = $operation.Params })
    Write-Json $responsePath ([ordered]@{ jsonrpc = "2.0"; id = $requestId; result = $operation.Result })
    $vectors.Add([ordered]@{ kind = "request"; name = $operation.Name; path = "examples/generated/request-$slug.json"; schemaRef = $requestSchemaRelative; valid = $true })
    $vectors.Add([ordered]@{ kind = "response"; name = $operation.Name; path = "examples/generated/response-$slug.json"; schemaRef = $responseSchemaRelative; valid = $true })
    $requestId++
}

foreach ($notification in $notifications) {
    $slug = $notification.Name.Replace('.', '-')
    $schemaRelative = "schema/generated/notification-$slug.schema.json"
    Write-Json (Join-Path $ProtocolRoot $schemaRelative) ([ordered]@{
        '$schema' = "https://json-schema.org/draft/2020-12/schema"
        allOf = @(
            [ordered]@{ '$ref' = "#/`$defs/notification" },
            [ordered]@{ properties = [ordered]@{ method = [ordered]@{ const = $notification.Name } } }
        )
        '$defs' = Get-DefinitionClosure @("notification")
    })
    Write-Json (Join-Path $ProtocolRoot "examples/generated/notification-$slug.json") ([ordered]@{ jsonrpc = "2.0"; method = $notification.Name; params = $notification.Params })
    $vectors.Add([ordered]@{ kind = "notification"; name = $notification.Name; path = "examples/generated/notification-$slug.json"; schemaRef = $schemaRelative; valid = $true })
}

foreach ($code in $errors) {
    $schemaRelative = "schema/generated/error-$code.schema.json"
    Write-Json (Join-Path $ProtocolRoot $schemaRelative) ([ordered]@{
        '$schema' = "https://json-schema.org/draft/2020-12/schema"
        allOf = @(
            [ordered]@{ '$ref' = "#/`$defs/errorResponse" },
            [ordered]@{ properties = [ordered]@{ error = [ordered]@{ properties = [ordered]@{ code = [ordered]@{ const = $code } } } } }
        )
        '$defs' = Get-DefinitionClosure @("errorResponse")
    })
    $protocolError = [ordered]@{ code = $code; message = "Stable protocol error" }
    if ($code -eq "consent_required") { $protocolError.data = [ordered]@{ endpoint = "https://api.openai.com/v1" } }
    if ($code -eq "protocol_mismatch") { $protocolError.data = [ordered]@{ missingCapabilities = @("ai.chat.context.v1", "ai.chat.branching.v1") } }
    Write-Json (Join-Path $ProtocolRoot "examples/generated/error-$code.json") ([ordered]@{ jsonrpc = "2.0"; id = 1; error = $protocolError })
    $vectors.Add([ordered]@{ kind = "error"; name = $code; path = "examples/generated/error-$code.json"; schemaRef = $schemaRelative; valid = $true })
}

$invalid = @(
    [ordered]@{ name = "handshake-missing-version"; path = "examples/invalid/handshake-missing-version.json"; schemaRef = "schema/generated/request-engine-handshake.schema.json" },
    [ordered]@{ name = "handshake-extra-field"; path = "examples/invalid/handshake-extra-field.json"; schemaRef = "schema/generated/request-engine-handshake.schema.json" },
    [ordered]@{ name = "handshake-duplicate-capability"; path = "examples/invalid/handshake-duplicate-capability.json"; schemaRef = "schema/generated/request-engine-handshake.schema.json" },
    [ordered]@{ name = "chat-send-missing-model"; path = "examples/invalid/chat-send-missing-model.json"; schemaRef = "schema/generated/request-ai-chat-send.schema.json" }
)
foreach ($item in $invalid) {
    $vectors.Add([ordered]@{ kind = "invalid"; name = $item.name; path = $item.path; schemaRef = $item.schemaRef; valid = $false })
}

$invalidIds = @(
    [ordered]@{ Name = "id-zero"; Id = 0 },
    [ordered]@{ Name = "id-negative"; Id = -1 },
    [ordered]@{ Name = "id-string"; Id = "1" },
    [ordered]@{ Name = "id-out-of-range"; Id = 2147483648 }
)
foreach ($item in $invalidIds) {
    $relativePath = "examples/generated/invalid-$($item.Name).json"
    Write-Json (Join-Path $ProtocolRoot $relativePath) ([ordered]@{ jsonrpc = "2.0"; id = $item.Id; method = "engine.handshake"; params = [ordered]@{ protocolVersion = "0.1"; contractRevision = 1; requiredCapabilities = @() } })
    $vectors.Add([ordered]@{ kind = "invalid"; name = $item.Name; path = $relativePath; schemaRef = "schema/generated/request-engine-handshake.schema.json"; valid = $false })
}

Write-Json (Join-Path $ProtocolRoot "examples/manifest.json") ([ordered]@{
    coverage = [ordered]@{ methods = $operations.Count; notifications = $notifications.Count; errors = $errors.Count }
    vectors = $vectors
})

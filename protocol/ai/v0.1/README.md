# Fowan AI protocol v0.1

This is the public, implementation-independent contract used by native Fowan clients to manage AI configuration and conversations. Provider routing, prompts, secrets, protected history, and orchestration remain private to FowanCore.

## Transport

Windows uses JSON-RPC 2.0 over a per-user, unguessable `fowan-core-v1-<token>` named pipe. The token is stored under the current user's `%LOCALAPPDATA%\\Fowan\\Core`; remote pipe clients are rejected. Each UTF-8 JSON message is framed with an ASCII header:

```text
Content-Length: <byte-count>\r\n
\r\n
<JSON body>
```

The machine-readable source of truth is `contract.json`. The generic envelope is defined by `schema/jsonrpc.schema.json`, shared strict DTO shapes are defined by `schema/methods.schema.json`, and method-specific request/response schemas are generated under `schema/generated`. Every compatibility vector declares its exact `schemaRef` in `examples/manifest.json`; regenerate the deterministic matrix with `scripts/generate-ai-protocol-fixtures.ps1` from the Fowan repository root.

Request and correlated response identifiers are positive 32-bit integers (`1..2147483647`). String, zero, negative, fractional, and out-of-range identifiers are invalid. An error response may use `null` only when no valid request identifier can be recovered.

The first request on every connection must be `engine.handshake` with `protocolVersion` and `requiredCapabilities`. The Core rejects all other methods until that handshake succeeds. AI Chat requires `ai.chat.v1`; AI Configuration requires `ai.config.v1`. A client is not required to request both capabilities.

The Core accepts multiple simultaneous clients. A transport or protocol failure is isolated to the offending connection, and chat cancellation identifiers are scoped to the client connection that created them.

## Configuration methods

- `ai.channels.list/create/update/delete`
- `ai.credentials.list/upsert/delete/test`
- `ai.models.list/upsert/delete/test/presets`
- `ai.toolFeatures.list`
- `ai.bindings.list/upsert/delete`
- `ai.consents.check/grant`

Credential write requests may contain a one-time `secret`. No response contains a secret or an internal secure-store locator. Models reference exactly one credential, and tool bindings reference a model belonging to the same credential.

## Conversation methods and events

- `ai.conversations.list/create/get/rename/delete`
- `ai.chat.send/cancel/regenerate`
- `ai.chat.started/delta/completed/cancelled/failed` notifications

`ai.chat.send` returns an invocation, conversation, and assistant-message identifier before streaming begins. Each delta contains only its invocation identifier and new text. Completed history responses contain the provider, credential display name, and model snapshot used for each assistant message, but never the credential secret.

## Stable errors

Clients localize the stable codes listed in `contract.json` without displaying raw provider bodies. Transport/session failures include `protocol_mismatch` and `handshake_required`; secure persistence failures include `secret_store_unavailable`, `protected_data_unavailable`, `storage_unavailable`, and `secure_state_inconsistent`.

Every provider network operation is Core-authorized. A missing endpoint grant returns `consent_required` with the normalized safe endpoint in `error.data.endpoint`; clients may ask the user, grant that endpoint, and retry once. `ai.bindings.upsert` accepts `featureId` and `modelProfileId`; the credential is derived from the model.

The unpublished v0.1 contract is strict: unknown fields, unknown methods, duplicate capabilities, and undeclared error codes are rejected. After v0.1 is released, changing a required field or its meaning requires a new protocol version; compatible additions must first be represented explicitly in the schemas and capability negotiation.

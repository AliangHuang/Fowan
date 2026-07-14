# Fowan AI protocol v0.1

This is the public, implementation-independent contract used by native Fowan clients to manage AI configuration and conversations. Provider routing, prompts, secrets, protected history, and orchestration remain private to FowanCore.

## Transport

Windows uses JSON-RPC 2.0 over a per-user, unguessable `fowan-core-v1-<token>` named pipe. The token is stored under the current user's `%LOCALAPPDATA%\\Fowan\\Core`; remote pipe clients are rejected. Each UTF-8 JSON message is framed with an ASCII header:

```text
Content-Length: <byte-count>\r\n
\r\n
<JSON body>
```

Clients must call `engine.handshake`, require protocol version `0.1`, and validate only the capability subset they use. AI Chat requires `ai.chat.v1`; AI Configuration requires `ai.config.v1`. A client is not required to request both capabilities.

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

Clients localize these codes without displaying raw provider bodies: `invalid_argument`, `not_found`, `conflict`, `protocol_mismatch`, `secret_store_unavailable`, `provider_auth_failed`, `provider_model_not_found`, `provider_rate_limited`, `provider_content_rejected`, `provider_unavailable`, `context_limit_exceeded`, `timeout`, and `cancelled`.

Additive fields are optional to older clients. Removing fields or changing their meaning requires a new major protocol version.

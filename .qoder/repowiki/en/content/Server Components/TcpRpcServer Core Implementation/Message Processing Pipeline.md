# Message Processing Pipeline

<cite>
**Referenced Files in This Document**
- [TcpRpcServer.cs](file://TcpRpc/Server/TcpRpcServer.cs)
- [NetworkService.cs](file://TcpRpc/Network/NetworkService.cs)
- [BinaryMessageSerializer.cs](file://TcpRpc/Formatter/BinaryMessageSerializer.cs)
- [MessageHeader.cs](file://TcpRpc/Message/MessageHeader.cs)
- [MessageType.cs](file://TcpRpc/Message/MessageType.cs)
- [RequestMessage.cs](file://TcpRpc/Message/RequestMessage.cs)
- [ResponseMessage.cs](file://TcpRpc/Message/ResponseMessage.cs)
- [AuthRequestMessage.cs](file://TcpRpc/Message/AuthRequestMessage.cs)
- [ServiceRegistry.cs](file://TcpRpc/Server/ServiceRegistry.cs)
- [ClientSessionManager.cs](file://TcpRpc/Server/Auth/ClientSessionManager.cs)
- [ClientSession.cs](file://TcpRpc/Server/Auth/ClientSession.cs)
- [SimpleTokenGenerator.cs](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs)
- [ServerAuthOptions.cs](file://TcpRpc/Server/ServerAuthOptions.cs)
- [IAuthProvider.cs](file://TcpRpc/Server/Auth/IAuthProvider.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Architecture Overview](#architecture-overview)
5. [Detailed Component Analysis](#detailed-component-analysis)
6. [Dependency Analysis](#dependency-analysis)
7. [Performance Considerations](#performance-considerations)
8. [Troubleshooting Guide](#troubleshooting-guide)
9. [Conclusion](#conclusion)

## Introduction
This document explains the TcpRpcServer message processing pipeline with a focus on the ProcessClientMessageAsync method. It covers how incoming messages are read via NetworkService, validated, routed to appropriate handlers, processed for authentication and requests, and serialized back to the client. It also documents the authentication workflow including token validation, session restoration, and renewal, as well as the service invocation path through ServiceRegistry. Practical examples illustrate typical message flows, authentication scenarios, and service invocation patterns. Serialization/deserialization and protocol compliance checks are explained to ensure robust interoperability.

## Project Structure
The message processing pipeline spans several core areas:
- Server orchestration and per-client handling
- Network transport utilities for header and body reads/writes
- Binary serialization for cross-process compatibility
- Message models and protocol types
- Authentication subsystem with sessions and token management
- Service registry for dynamic invocation

```mermaid
graph TB
subgraph "Server"
S["TcpRpcServer"]
SR["ServiceRegistry"]
ASM["ServerAuthOptions"]
CSM["ClientSessionManager"]
end
subgraph "Network"
NS["NetworkService"]
MH["MessageHeader"]
end
subgraph "Serialization"
BMS["BinaryMessageSerializer"]
end
subgraph "Messages"
AR["AuthRequestMessage"]
RM["RequestMessage"]
RSP["ResponseMessage"]
MT["MessageType"]
end
subgraph "Auth"
ISG["IAuthTokenGenerator"]
STG["SimpleTokenGenerator"]
CS["ClientSession"]
IAP["IAuthProvider"]
end
S --> NS
S --> BMS
S --> SR
S --> ASM
S --> CSM
NS --> MH
BMS --> AR
BMS --> RM
BMS --> RSP
MH --> MT
CSM --> CS
ASM --> ISG
ISG --> STG
ASM --> IAP
```

**Diagram sources**
- [TcpRpcServer.cs:15-707](file://TcpRpc/Server/TcpRpcServer.cs#L15-L707)
- [NetworkService.cs:7-99](file://TcpRpc/Network/NetworkService.cs#L7-L99)
- [BinaryMessageSerializer.cs:11-444](file://TcpRpc/Formatter/BinaryMessageSerializer.cs#L11-L444)
- [MessageHeader.cs:9-73](file://TcpRpc/Message/MessageHeader.cs#L9-L73)
- [MessageType.cs:3-13](file://TcpRpc/Message/MessageType.cs#L3-L13)
- [RequestMessage.cs:6-18](file://TcpRpc/Message/RequestMessage.cs#L6-L18)
- [ResponseMessage.cs:6-30](file://TcpRpc/Message/ResponseMessage.cs#L6-L30)
- [AuthRequestMessage.cs:6-28](file://TcpRpc/Message/AuthRequestMessage.cs#L6-L28)
- [ServiceRegistry.cs:10-272](file://TcpRpc/Server/ServiceRegistry.cs#L10-L272)
- [ClientSessionManager.cs:9-188](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L9-L188)
- [ClientSession.cs:6-95](file://TcpRpc/Server/Auth/ClientSession.cs#L6-L95)
- [SimpleTokenGenerator.cs:11-163](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L11-L163)
- [ServerAuthOptions.cs:8-40](file://TcpRpc/Server/ServerAuthOptions.cs#L8-L40)
- [IAuthProvider.cs:32-41](file://TcpRpc/Server/Auth/IAuthProvider.cs#L32-L41)

**Section sources**
- [TcpRpcServer.cs:15-707](file://TcpRpc/Server/TcpRpcServer.cs#L15-L707)
- [NetworkService.cs:7-99](file://TcpRpc/Network/NetworkService.cs#L7-L99)
- [BinaryMessageSerializer.cs:11-444](file://TcpRpc/Formatter/BinaryMessageSerializer.cs#L11-L444)
- [MessageHeader.cs:9-73](file://TcpRpc/Message/MessageHeader.cs#L9-L73)
- [MessageType.cs:3-13](file://TcpRpc/Message/MessageType.cs#L3-L13)
- [RequestMessage.cs:6-18](file://TcpRpc/Message/RequestMessage.cs#L6-L18)
- [ResponseMessage.cs:6-30](file://TcpRpc/Message/ResponseMessage.cs#L6-L30)
- [AuthRequestMessage.cs:6-28](file://TcpRpc/Message/AuthRequestMessage.cs#L6-L28)
- [ServiceRegistry.cs:10-272](file://TcpRpc/Server/ServiceRegistry.cs#L10-L272)
- [ClientSessionManager.cs:9-188](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L9-L188)
- [ClientSession.cs:6-95](file://TcpRpc/Server/Auth/ClientSession.cs#L6-L95)
- [SimpleTokenGenerator.cs:11-163](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L11-L163)
- [ServerAuthOptions.cs:8-40](file://TcpRpc/Server/ServerAuthOptions.cs#L8-L40)
- [IAuthProvider.cs:32-41](file://TcpRpc/Server/Auth/IAuthProvider.cs#L32-L41)

## Core Components
- TcpRpcServer orchestrates connection acceptance, per-client message loops, and dispatches to ProcessClientMessageAsync. It manages authentication state via ClientSessionContext and enforces auth policies from ServerAuthOptions.
- NetworkService provides protocol-compliant read/write helpers for message headers and bodies, including length checks and safe buffering.
- BinaryMessageSerializer implements compact binary encoding for RequestMessage, ResponseMessage, and Auth messages with type markers and JSON fallback for complex objects.
- MessageHeader carries type, message ID, content length, flags, and optional token for extended auth support.
- ServiceRegistry resolves services and methods, converts parameters, builds interceptor chains, and invokes target methods.
- ClientSessionManager maintains sessions, token-to-session mapping, and periodic cleanup.
- SimpleTokenGenerator implements JWT-like signing and validation with Base64Url encoding and HMAC-SHA256 signatures.

**Section sources**
- [TcpRpcServer.cs:226-451](file://TcpRpc/Server/TcpRpcServer.cs#L226-L451)
- [NetworkService.cs:30-99](file://TcpRpc/Network/NetworkService.cs#L30-L99)
- [BinaryMessageSerializer.cs:30-442](file://TcpRpc/Formatter/BinaryMessageSerializer.cs#L30-L442)
- [MessageHeader.cs:41-73](file://TcpRpc/Message/MessageHeader.cs#L41-L73)
- [ServiceRegistry.cs:72-138](file://TcpRpc/Server/ServiceRegistry.cs#L72-L138)
- [ClientSessionManager.cs:27-188](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L27-L188)
- [SimpleTokenGenerator.cs:38-163](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L38-L163)

## Architecture Overview
The server runs a continuous loop per client, reading a 4-byte little-endian header length followed by the serialized MessageHeader, then the content bytes sized by ContentLength. The server validates lengths, routes by MessageType, and responds accordingly.

```mermaid
sequenceDiagram
participant C as "Client"
participant S as "TcpRpcServer"
participant NS as "NetworkService"
participant SER as "BinaryMessageSerializer"
participant REG as "ServiceRegistry"
C->>S : "TCP connect"
loop "Per message"
S->>NS : "ReadMessageHeaderAsync()"
NS-->>S : "MessageHeader"
S->>S : "Validate ContentLength"
alt "ContentLength > 0"
S->>NS : "ReadBytesAsync(ContentLength)"
NS-->>S : "Content bytes"
else "ContentLength == 0"
S->>S : "Empty content buffer"
end
S->>S : "ProcessClientMessageAsync(header, content)"
alt "AuthRequest"
S->>SER : "DeserializeAuthRequest"
S->>S : "ProcessAuthRequestAsync"
S->>SER : "SerializeAuthResponse"
S->>NS : "WriteMessageAsync(header, data)"
else "Request"
S->>S : "Check auth and token"
S->>SER : "DeserializeRequest"
S->>REG : "InvokeAsync(ServiceType, Method, Params)"
REG-->>S : "MethodResult"
S->>SER : "SerializeResponse"
S->>NS : "WriteMessageAsync(header, data)"
else "Ping"
S->>NS : "WriteMessageAsync(Pong)"
else "AuthRenew"
S->>S : "Generate new token"
S->>SER : "SerializeAuthResponse"
S->>NS : "WriteMessageAsync(header, data)"
end
end
```

**Diagram sources**
- [TcpRpcServer.cs:247-451](file://TcpRpc/Server/TcpRpcServer.cs#L247-L451)
- [NetworkService.cs:30-52](file://TcpRpc/Network/NetworkService.cs#L30-L52)
- [BinaryMessageSerializer.cs:50-442](file://TcpRpc/Formatter/BinaryMessageSerializer.cs#L50-L442)
- [ServiceRegistry.cs:72-138](file://TcpRpc/Server/ServiceRegistry.cs#L72-L138)

## Detailed Component Analysis

### ProcessClientMessageAsync: Message Routing and Dispatch
ProcessClientMessageAsync is the central dispatcher for incoming messages. It:
- Deserializes AuthRequest, Request, or reads Ping/Pong tokens from the header.
- Enforces authentication preconditions and validates tokens when present.
- Invokes ProcessAuthRequestAsync for AuthRequest, ProcessRequestAsync for Request, sends Pong for Ping, and generates renewed tokens for AuthRenew.
- Updates session activity on successful requests.

```mermaid
flowchart TD
Start(["Entry: ProcessClientMessageAsync"]) --> ReadHeader["Read MessageHeader"]
ReadHeader --> TypeSwitch{"MessageType"}
TypeSwitch --> |AuthRequest| DeserializeAuth["DeserializeAuthRequest"]
DeserializeAuth --> AuthFlow["ProcessAuthRequestAsync"]
AuthFlow --> SendAuthResp["SendAuthResponseAsync"]
SendAuthResp --> End(["Exit"])
TypeSwitch --> |Request| CheckAuth["Check auth flag and context"]
CheckAuth --> TokenCheck["Validate Token if present"]
TokenCheck --> DeserializeReq["DeserializeRequest"]
DeserializeReq --> InvokeSvc["ServiceRegistry.InvokeAsync"]
InvokeSvc --> SendResp["SendResponseAsync"]
SendResp --> Touch["Touch session"]
Touch --> End
TypeSwitch --> |Ping| SendPong["SendPongAsync"]
SendPong --> End
TypeSwitch --> |AuthRenew| Renew["Generate new token and update mapping"]
Renew --> SendAuthResp2["SendAuthResponseAsync"]
SendAuthResp2 --> End
```

**Diagram sources**
- [TcpRpcServer.cs:380-451](file://TcpRpc/Server/TcpRpcServer.cs#L380-L451)
- [ServiceRegistry.cs:72-138](file://TcpRpc/Server/ServiceRegistry.cs#L72-L138)

**Section sources**
- [TcpRpcServer.cs:380-451](file://TcpRpc/Server/TcpRpcServer.cs#L380-L451)

### Message Reading and Protocol Compliance
NetworkService.ReadMessageHeaderAsync:
- Reads a 4-byte little-endian header size, validates it is positive and reasonable.
- Reads the serialized MessageHeader and deserializes it.
- Throws on invalid sizes to prevent malformed frames.

NetworkService.ReadBytesAsync:
- Safely reads content bytes with defensive checks.
- For large payloads (> 100KB), drains and returns empty buffer to signal oversized content.
- Returns null on EOF to indicate connection closure.

TcpRpcServer.HandleClientAsync:
- Uses NetworkService.ReadMessageHeaderAsync and ReadBytesAsync to extract header and content.
- Validates ContentLength and handles oversized content by sending an auth error response and continuing.
- Calls ProcessClientMessageAsync for each complete message.

```mermaid
sequenceDiagram
participant S as "TcpRpcServer"
participant NS as "NetworkService"
participant Stream as "NetworkStream"
S->>NS : "ReadMessageHeaderAsync(Stream)"
NS->>Stream : "Read 4 bytes header size"
NS->>Stream : "Read header bytes"
NS-->>S : "MessageHeader or null"
alt "Header OK"
S->>S : "Validate ContentLength"
alt "ContentLength > 0"
S->>NS : "ReadBytesAsync(Stream, ContentLength)"
NS->>Stream : "Read content bytes"
NS-->>S : "Content bytes or [] (oversized)"
else "ContentLength == 0"
S->>S : "Empty buffer"
end
S->>S : "ProcessClientMessageAsync(...)"
else "Header null/invalid"
S->>S : "Log and exit client loop"
end
```

**Diagram sources**
- [TcpRpcServer.cs:247-286](file://TcpRpc/Server/TcpRpcServer.cs#L247-L286)
- [NetworkService.cs:30-52](file://TcpRpc/Network/NetworkService.cs#L30-L52)
- [NetworkService.cs:55-99](file://TcpRpc/Network/NetworkService.cs#L55-L99)

**Section sources**
- [TcpRpcServer.cs:247-286](file://TcpRpc/Server/TcpRpcServer.cs#L247-L286)
- [NetworkService.cs:30-52](file://TcpRpc/Network/NetworkService.cs#L30-L52)
- [NetworkService.cs:55-99](file://TcpRpc/Network/NetworkService.cs#L55-L99)

### Authentication Workflow: Validation, Restoration, Renewal
ProcessAuthRequestAsync:
- If authentication is disabled, returns success with a generated ClientId.
- If reconnect with PreviousToken is requested, attempts session restoration via ClientSessionManager.TryRestoreSession.
- On failure, performs credential-based authentication via IAuthProvider.ValidateAsync.
- Creates a new session, populates claims, generates a token with SimpleTokenGenerator, updates token-to-session mapping, and sets context session.

Token Validation and Renewal:
- For Request messages, if a token is present in the header, TcpRpcServer validates it using IAuthTokenGenerator.ValidateToken.
- For AuthRenew, the server regenerates a token, updates session info, and writes an AuthResponse.

```mermaid
sequenceDiagram
participant C as "Client"
participant S as "TcpRpcServer"
participant ASM as "ServerAuthOptions"
participant AP as "IAuthProvider"
participant CSM as "ClientSessionManager"
participant SG as "SimpleTokenGenerator"
C->>S : "AuthRequest(ClientId?, PreviousToken?, Credentials?)"
alt "RequireAuth == false"
S-->>C : "AuthResponse(Success=true, ClientId)"
else "RequireAuth == true"
alt "IsReconnect && PreviousToken"
S->>CSM : "TryRestoreSession(ClientId, PreviousToken)"
CSM-->>S : "Session or null"
alt "Restored"
S->>SG : "GenerateToken(Session.Claims, Lifetime)"
S->>CSM : "UpdateTokenMapping(token, sessionId)"
S-->>C : "AuthResponse(Success=true, IsSessionRestored=true)"
else "Not restored"
S->>AP : "ValidateAsync(Credentials)"
AP-->>S : "AuthResult"
S->>CSM : "CreateSession(AuthResult.ClientId)"
S->>SG : "GenerateToken(Session.Claims, Lifetime)"
S->>CSM : "UpdateTokenMapping(token, sessionId)"
S-->>C : "AuthResponse(Success=true, IsSessionRestored=false)"
end
else "First-time auth"
S->>AP : "ValidateAsync(Credentials)"
AP-->>S : "AuthResult"
S->>CSM : "CreateSession(AuthResult.ClientId)"
S->>SG : "GenerateToken(Session.Claims, Lifetime)"
S->>CSM : "UpdateTokenMapping(token, sessionId)"
S-->>C : "AuthResponse(Success=true)"
end
end
```

**Diagram sources**
- [TcpRpcServer.cs:453-547](file://TcpRpc/Server/TcpRpcServer.cs#L453-L547)
- [ClientSessionManager.cs:88-116](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L88-L116)
- [SimpleTokenGenerator.cs:38-77](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L38-L77)
- [IAuthProvider.cs:32-41](file://TcpRpc/Server/Auth/IAuthProvider.cs#L32-L41)

**Section sources**
- [TcpRpcServer.cs:396-449](file://TcpRpc/Server/TcpRpcServer.cs#L396-L449)
- [TcpRpcServer.cs:453-547](file://TcpRpc/Server/TcpRpcServer.cs#L453-L547)
- [ClientSessionManager.cs:88-116](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L88-L116)
- [SimpleTokenGenerator.cs:38-77](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L38-L77)
- [IAuthProvider.cs:32-41](file://TcpRpc/Server/Auth/IAuthProvider.cs#L32-L41)

### Request Processing Through ServiceRegistry
ProcessRequestAsync:
- First checks legacy RequestHandler registrations by Method.
- Falls back to ServiceRegistry.InvokeAsync with ServiceType, Method, Parameters, and ParameterTypes.
- Converts parameters to target method signatures and unwraps Task<T> return types.
- Builds an interceptor chain around the target invocation.
- Returns a ResponseMessage with either Data/DataType/IsVoid on success or Error/ExceptionType/StackTrace on failure.

```mermaid
sequenceDiagram
participant S as "TcpRpcServer"
participant REG as "ServiceRegistry"
participant INT as "IInterceptor chain"
participant SVC as "Service Instance"
S->>REG : "InvokeAsync(ServiceType, Method, Params, ParamTypes)"
REG->>REG : "ConvertParameters"
REG->>INT : "BuildInterceptorChain"
INT->>SVC : "Invoke method"
SVC-->>INT : "Return value or Task"
INT-->>REG : "Unwrap Task<T> if needed"
REG-->>S : "MethodResult"
S-->>S : "Map MethodResult -> ResponseMessage"
```

**Diagram sources**
- [TcpRpcServer.cs:549-614](file://TcpRpc/Server/TcpRpcServer.cs#L549-L614)
- [ServiceRegistry.cs:72-138](file://TcpRpc/Server/ServiceRegistry.cs#L72-L138)

**Section sources**
- [TcpRpcServer.cs:549-614](file://TcpRpc/Server/TcpRpcServer.cs#L549-L614)
- [ServiceRegistry.cs:72-138](file://TcpRpc/Server/ServiceRegistry.cs#L72-L138)

### Serialization, Deserialization, and Protocol Compliance
BinaryMessageSerializer:
- Encodes RequestMessage with ServiceType, Method, Parameters, and ParameterTypes.
- Encodes ResponseMessage with Success/Error/ExceptionType/StackTrace/Data/DataType/IsVoid.
- Encodes Auth messages with ClientId, PreviousToken, Credentials, and AuthResponse fields.
- Uses type markers and UTF-8 strings, with JSON fallback for complex objects and assembly-qualified type names for deserialization.

Protocol compliance:
- MessageHeader includes Type, MessageId, ContentLength, Flags, and optional Token.
- NetworkService enforces header size limits and content length sanity checks.
- TcpRpcServer validates ContentLength and handles oversized content gracefully.

```mermaid
classDiagram
class BinaryMessageSerializer {
+SerializeRequest(request)
+DeserializeRequest(data)
+SerializeResponse(response)
+DeserializeResponse(data)
+SerializeAuthRequest(req)
+DeserializeAuthRequest(data)
+SerializeAuthResponse(resp)
+DeserializeAuthResponse(data)
}
class MessageHeader {
+MessageType Type
+long MessageId
+int ContentLength
+byte Flags
+string Token
+Serialize()
+Deserialize(buffer)
}
class RequestMessage {
+string ServiceType
+string Method
+object[] Parameters
+string[] ParameterTypes
}
class ResponseMessage {
+bool Success
+string Error
+string ExceptionType
+string StackTrace
+object Data
+string DataType
+bool IsVoid
}
BinaryMessageSerializer --> MessageHeader : "serializes/deserializes"
BinaryMessageSerializer --> RequestMessage : "handles"
BinaryMessageSerializer --> ResponseMessage : "handles"
```

**Diagram sources**
- [BinaryMessageSerializer.cs:30-442](file://TcpRpc/Formatter/BinaryMessageSerializer.cs#L30-L442)
- [MessageHeader.cs:41-73](file://TcpRpc/Message/MessageHeader.cs#L41-L73)
- [RequestMessage.cs:6-18](file://TcpRpc/Message/RequestMessage.cs#L6-L18)
- [ResponseMessage.cs:6-30](file://TcpRpc/Message/ResponseMessage.cs#L6-L30)

**Section sources**
- [BinaryMessageSerializer.cs:30-442](file://TcpRpc/Formatter/BinaryMessageSerializer.cs#L30-L442)
- [MessageHeader.cs:41-73](file://TcpRpc/Message/MessageHeader.cs#L41-L73)
- [RequestMessage.cs:6-18](file://TcpRpc/Message/RequestMessage.cs#L6-L18)
- [ResponseMessage.cs:6-30](file://TcpRpc/Message/ResponseMessage.cs#L6-L30)

### Practical Examples

- Reception to Response (successful Request):
  - Client sends a Request with ServiceType, Method, Parameters, and ParameterTypes.
  - Server deserializes, authenticates if required, invokes ServiceRegistry, serializes Response, and writes Pong/Error depending on Success.

- Authentication Flow (reconnect):
  - Client sends AuthRequest with PreviousToken and ClientId.
  - Server attempts session restoration; if successful, generates a new token and returns AuthResponse with IsSessionRestored.

- Authentication Flow (first-time):
  - Client sends AuthRequest with Credentials.
  - Server validates credentials, creates a session, generates token, and returns AuthResponse.

- Ping/Pong:
  - Client sends Ping; Server responds with Pong.

- AuthRenew:
  - Client sends AuthRenew; Server generates a new token and returns AuthResponse.

**Section sources**
- [TcpRpcServer.cs:396-449](file://TcpRpc/Server/TcpRpcServer.cs#L396-L449)
- [TcpRpcServer.cs:425-427](file://TcpRpc/Server/TcpRpcServer.cs#L425-L427)
- [TcpRpcServer.cs:429-449](file://TcpRpc/Server/TcpRpcServer.cs#L429-L449)
- [ServiceRegistry.cs:72-138](file://TcpRpc/Server/ServiceRegistry.cs#L72-L138)

## Dependency Analysis
TcpRpcServer depends on:
- NetworkService for protocol framing and IO safety.
- BinaryMessageSerializer for cross-process message encoding/decoding.
- ServerAuthOptions for policy decisions (RequireAuth, TokenLifetime, SessionTimeout).
- ClientSessionManager for session lifecycle and token mapping.
- ServiceRegistry for dynamic service invocation.

```mermaid
graph LR
S["TcpRpcServer"] --> NS["NetworkService"]
S --> BMS["BinaryMessageSerializer"]
S --> ASM["ServerAuthOptions"]
S --> CSM["ClientSessionManager"]
S --> SR["ServiceRegistry"]
ASM --> STG["SimpleTokenGenerator"]
ASM --> IAP["IAuthProvider"]
```

**Diagram sources**
- [TcpRpcServer.cs:66-74](file://TcpRpc/Server/TcpRpcServer.cs#L66-L74)
- [ServerAuthOptions.cs:8-40](file://TcpRpc/Server/ServerAuthOptions.cs#L8-L40)
- [ClientSessionManager.cs:9-25](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L9-L25)
- [ServiceRegistry.cs:10-19](file://TcpRpc/Server/ServiceRegistry.cs#L10-L19)
- [SimpleTokenGenerator.cs:11-36](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L11-L36)
- [IAuthProvider.cs:32-41](file://TcpRpc/Server/Auth/IAuthProvider.cs#L32-L41)

**Section sources**
- [TcpRpcServer.cs:66-74](file://TcpRpc/Server/TcpRpcServer.cs#L66-L74)
- [ServerAuthOptions.cs:8-40](file://TcpRpc/Server/ServerAuthOptions.cs#L8-L40)
- [ClientSessionManager.cs:9-25](file://TcpRpc/Server/Auth/ClientSessionManager.cs#L9-L25)
- [ServiceRegistry.cs:10-19](file://TcpRpc/Server/ServiceRegistry.cs#L10-L19)
- [SimpleTokenGenerator.cs:11-36](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L11-L36)
- [IAuthProvider.cs:32-41](file://TcpRpc/Server/Auth/IAuthProvider.cs#L32-L41)

## Performance Considerations
- Large payload handling: NetworkService and TcpRpcServer drop oversized content after draining to avoid corrupting subsequent messages. Prefer chunked uploads or streaming for very large payloads outside the scope of this protocol.
- Buffering: ReadBytesAsync uses fixed-size buffers for large reads to limit memory spikes.
- Session cleanup: ClientSessionManager periodically removes expired sessions to keep memory footprint bounded.
- Interceptor overhead: ServiceRegistry builds an interceptor chain; keep interceptors focused to minimize latency.

[No sources needed since this section provides general guidance]

## Troubleshooting Guide
Common issues and diagnostics:
- Invalid header size or malformed header: NetworkService throws on invalid header size; TcpRpcServer logs and exits client loop.
- Negative or excessive ContentLength: TcpRpcServer validates and returns on invalid values; oversized content triggers an auth error response and continues.
- Authentication failures: ProcessAuthRequestAsync returns AuthResponse with error; check IAuthProvider implementation and credentials.
- Token validation errors: ValidateToken returns invalid with error message; ensure token format and signature are correct and not expired.
- Service not found or method not found: ServiceRegistry.InvokeAsync returns failure; verify ServiceType and Method names match registration.
- Handler errors: ProcessRequestAsync captures exceptions and returns error response with exception metadata.

**Section sources**
- [NetworkService.cs:39-43](file://TcpRpc/Network/NetworkService.cs#L39-L43)
- [TcpRpcServer.cs:255-281](file://TcpRpc/Server/TcpRpcServer.cs#L255-L281)
- [TcpRpcServer.cs:505-512](file://TcpRpc/Server/TcpRpcServer.cs#L505-L512)
- [SimpleTokenGenerator.cs:79-137](file://TcpRpc/Server/Auth/SimpleTokenGenerator.cs#L79-L137)
- [ServiceRegistry.cs:74-82](file://TcpRpc/Server/ServiceRegistry.cs#L74-L82)
- [TcpRpcServer.cs:604-613](file://TcpRpc/Server/TcpRpcServer.cs#L604-L613)

## Conclusion
The TcpRpcServer implements a robust, protocol-compliant message processing pipeline. It safely reads and validates messages, routes them by type, enforces authentication policies, and executes service invocations with strong serialization guarantees. The design balances extensibility (interceptors, custom auth providers) with operational safety (length checks, session cleanup, graceful error handling).
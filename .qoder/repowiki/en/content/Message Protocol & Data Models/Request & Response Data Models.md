# Request & Response Data Models

<cite>
**Referenced Files in This Document**
- [RequestMessage.cs](file://TcpRpc/Message/RequestMessage.cs)
- [ResponseMessage.cs](file://TcpRpc/Message/ResponseMessage.cs)
- [JsonMessageSerializer.cs](file://TcpRpc/Formatter/JsonMessageSerializer.cs)
- [IMessageSerializer.cs](file://TcpRpc/Formatter/IMessageSerializer.cs)
- [MessageHeader.cs](file://TcpRpc/Message/MessageHeader.cs)
- [MessageType.cs](file://TcpRpc/Message/MessageType.cs)
- [TcpRpcClient.cs](file://TcpRpc/Client/TcpRpcClient.cs)
- [RpcClientState.cs](file://TcpRpc/Client/RpcClientState.cs)
- [TcpRpcServer.cs](file://TcpRpc/Server/TcpRpcServer.cs)
- [RpcServerState.cs](file://TcpRpc/Server/RpcServerState.cs)
- [RequestHandler.cs](file://TcpRpc/Server/RequestHandler.cs)
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
This document provides comprehensive documentation for the RequestMessage and ResponseMessage data models that define the core payload structures in TcpRpc. It explains the properties and semantics of each model, the serialization and deserialization pipeline, correlation mechanisms between requests and responses, timeout handling, and cleanup procedures. It also covers payload size limits, JSON formatting expectations, and performance considerations for large payloads and streaming scenarios.

## Project Structure
The TcpRpc library organizes message models, serialization, and transport framing under dedicated namespaces:
- Message models: RequestMessage and ResponseMessage
- Serialization: JsonMessageSerializer implementing IMessageSerializer
- Transport framing: MessageHeader and MessageType enums
- Client and server orchestration: RpcClient and RpcServer manage request/response lifecycle, timeouts, and cleanup

```mermaid
graph TB
subgraph "Message Layer"
RM["RequestMessage"]
RSM["ResponseMessage"]
MH["MessageHeader"]
MT["MessageType"]
end
subgraph "Serialization"
IS["IMessageSerializer"]
JMS["JsonMessageSerializer"]
end
subgraph "Transport"
TCPCL["RpcClient"]
TCPS["RpcServer"]
end
RM --> JMS
RSM --> JMS
JMS --> IS
MH --> TCPCL
MH --> TCPS
MT --> MH
TCPCL --> MH
TCPCL --> JMS
TCPS --> MH
TCPS --> JMS
```

**Diagram sources**
- [RequestMessage.cs:1-8](file://TcpRpc/Message/RequestMessage.cs#L1-L8)
- [ResponseMessage.cs:1-9](file://TcpRpc/Message/ResponseMessage.cs#L1-L9)
- [JsonMessageSerializer.cs:1-85](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L1-L85)
- [IMessageSerializer.cs:1-13](file://TcpRpc/Formatter/IMessageSerializer.cs#L1-L13)
- [MessageHeader.cs:1-34](file://TcpRpc/Message/MessageHeader.cs#L1-L34)
- [MessageType.cs:1-10](file://TcpRpc/Message/MessageType.cs#L1-L10)
- [TcpRpcClient.cs:1-405](file://TcpRpc/Client/TcpRpcClient.cs#L1-L405)
- [TcpRpcServer.cs:1-432](file://TcpRpc/Server/TcpRpcServer.cs#L1-L432)

**Section sources**
- [RequestMessage.cs:1-8](file://TcpRpc/Message/RequestMessage.cs#L1-L8)
- [ResponseMessage.cs:1-9](file://TcpRpc/Message/ResponseMessage.cs#L1-L9)
- [JsonMessageSerializer.cs:1-85](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L1-L85)
- [IMessageSerializer.cs:1-13](file://TcpRpc/Formatter/IMessageSerializer.cs#L1-L13)
- [MessageHeader.cs:1-34](file://TcpRpc/Message/MessageHeader.cs#L1-L34)
- [MessageType.cs:1-10](file://TcpRpc/Message/MessageType.cs#L1-L10)
- [TcpRpcClient.cs:1-405](file://TcpRpc/Client/TcpRpcClient.cs#L1-L405)
- [TcpRpcServer.cs:1-432](file://TcpRpc/Server/TcpRpcServer.cs#L1-L432)

## Core Components
This section documents the two primary data models and their roles in the RPC protocol.

- RequestMessage
  - Purpose: Encapsulates a remote procedure call request.
  - Properties:
    - MessageId: Correlation identifier for request-response pairing.
    - Method: Target method name for the server-side handler.
    - Data: Serialized parameter payload as UTF-8 bytes.
  - Validation: Deserialization enforces non-empty Method and positive MessageId.

- ResponseMessage
  - Purpose: Encapsulates a response to a request.
  - Properties:
    - MessageId: Must match the originating request’s MessageId.
    - Success: Boolean indicating whether the operation succeeded.
    - Error: Human-readable error message when Success is false.
    - Data: Serialized return payload as UTF-8 bytes when Success is true.

- Serialization
  - JsonMessageSerializer serializes/deserializes RequestMessage and ResponseMessage using System.Text.Json with camelCase property naming and compact output.
  - Both models are validated post-deserialization for structural correctness.

- Transport Framing
  - MessageHeader carries Type, MessageId, and ContentLength for each TCP frame.
  - MessageType enumerates Request, Response, Ping, Pong, and Error frames.

**Section sources**
- [RequestMessage.cs:1-8](file://TcpRpc/Message/RequestMessage.cs#L1-L8)
- [ResponseMessage.cs:1-9](file://TcpRpc/Message/ResponseMessage.cs#L1-L9)
- [JsonMessageSerializer.cs:1-85](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L1-L85)
- [MessageHeader.cs:1-34](file://TcpRpc/Message/MessageHeader.cs#L1-L34)
- [MessageType.cs:1-10](file://TcpRpc/Message/MessageType.cs#L1-L10)

## Architecture Overview
The request-response flow integrates message models, serialization, and transport framing across client and server.

```mermaid
sequenceDiagram
participant Client as "RpcClient"
participant Serializer as "JsonMessageSerializer"
participant Header as "MessageHeader"
participant Server as "RpcServer"
Client->>Client : "Build RequestMessage"
Client->>Serializer : "SerializeRequest(request)"
Serializer-->>Client : "UTF-8 bytes"
Client->>Header : "Create header(Type=Request,<br/>MessageId,ContentLength)"
Client->>Server : "Send header + payload"
Server->>Serializer : "DeserializeRequest(payload)"
Serializer-->>Server : "RequestMessage"
Server->>Server : "Lookup handler by Method"
Server->>Server : "Execute handler"
Server->>Serializer : "SerializeResponse(response)"
Serializer-->>Server : "UTF-8 bytes"
Server->>Header : "Create header(Type=Response/Error,<br/>MessageId,ContentLength)"
Server->>Client : "Send header + payload"
Client->>Serializer : "DeserializeResponse(payload)"
Serializer-->>Client : "ResponseMessage"
Client->>Client : "Resolve TaskCompletionSource with ResponseMessage"
```

**Diagram sources**
- [TcpRpcClient.cs:132-189](file://TcpRpc/Client/TcpRpcClient.cs#L132-L189)
- [TcpRpcServer.cs:318-376](file://TcpRpc/Server/TcpRpcServer.cs#L318-L376)
- [JsonMessageSerializer.cs:14-84](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L14-L84)
- [MessageHeader.cs:13-33](file://TcpRpc/Message/MessageHeader.cs#L13-L33)

## Detailed Component Analysis

### RequestMessage Model
- Structure and purpose:
  - Holds the correlation MessageId, target Method, and serialized Data payload.
- Serialization expectations:
  - Data is treated as raw UTF-8 bytes; clients typically serialize parameters using System.Text.Json before constructing the RequestMessage.
- Validation:
  - Deserialization validates Method is non-empty and MessageId is positive.

```mermaid
classDiagram
class RequestMessage {
+long MessageId
+string Method
+byte[] Data
}
class JsonMessageSerializer {
+SerializeRequest(request) byte[]
+DeserializeRequest(data) RequestMessage
}
RequestMessage <-- JsonMessageSerializer : "serialized/deserialized"
```

**Diagram sources**
- [RequestMessage.cs:1-8](file://TcpRpc/Message/RequestMessage.cs#L1-L8)
- [JsonMessageSerializer.cs:14-49](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L14-L49)

**Section sources**
- [RequestMessage.cs:1-8](file://TcpRpc/Message/RequestMessage.cs#L1-L8)
- [JsonMessageSerializer.cs:14-49](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L14-L49)

### ResponseMessage Model
- Structure and purpose:
  - Carries correlation MessageId, success/failure flag, optional error message, and optional serialized return payload.
- Propagation of errors:
  - On handler exceptions or invalid requests, Success is false and Error contains a descriptive message.
- Serialization expectations:
  - Data is serialized UTF-8 bytes; clients deserialize to strongly typed results when Success is true.

```mermaid
classDiagram
class ResponseMessage {
+long MessageId
+bool Success
+string Error
+byte[] Data
}
class JsonMessageSerializer {
+SerializeResponse(response) byte[]
+DeserializeResponse(data) ResponseMessage
}
ResponseMessage <-- JsonMessageSerializer : "serialized/deserialized"
```

**Diagram sources**
- [ResponseMessage.cs:1-9](file://TcpRpc/Message/ResponseMessage.cs#L1-L9)
- [JsonMessageSerializer.cs:51-84](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L51-L84)

**Section sources**
- [ResponseMessage.cs:1-9](file://TcpRpc/Message/ResponseMessage.cs#L1-L9)
- [JsonMessageSerializer.cs:51-84](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L51-L84)

### Transport Framing and Message Types
- MessageHeader:
  - Fixed-size header with Type, MessageId, and ContentLength.
  - Provides Serialize/Deserialize helpers for little-endian binary encoding.
- MessageType:
  - Enumerates Request, Response, Ping, Pong, and Error frames.

```mermaid
classDiagram
class MessageHeader {
+const int HeaderSize
+MessageType Type
+long MessageId
+int ContentLength
+Serialize() byte[]
+Deserialize(buffer) MessageHeader
}
class MessageType {
<<enumeration>>
Request
Response
Ping
Pong
Error
}
MessageHeader --> MessageType : "uses"
```

**Diagram sources**
- [MessageHeader.cs:1-34](file://TcpRpc/Message/MessageHeader.cs#L1-L34)
- [MessageType.cs:1-10](file://TcpRpc/Message/MessageType.cs#L1-L10)

**Section sources**
- [MessageHeader.cs:1-34](file://TcpRpc/Message/MessageHeader.cs#L1-L34)
- [MessageType.cs:1-10](file://TcpRpc/Message/MessageType.cs#L1-L10)

### Client-Side Request Construction and Response Handling
- Request construction:
  - RpcClient constructs RequestMessage with an auto-incremented MessageId, sets Method, and serializes parameters to Data.
  - Serializes RequestMessage to UTF-8 bytes and sends a Request frame with MessageHeader.
- Response handling:
  - RpcClient maintains a concurrent dictionary keyed by MessageId to resolve TaskCompletionSource upon receiving a Response frame.
  - On timeout or connection loss, pending requests are canceled or faulted.
- Timeout handling:
  - SendRequestAsync uses a linked cancellation token with a configurable timeout; on cancellation, the pending request is removed and a TimeoutException is thrown.
- Cleanup:
  - DisconnectAsync clears pending requests and transitions state to Disconnected.
  - Dispose ensures graceful shutdown and resource release.

```mermaid
sequenceDiagram
participant Client as "RpcClient"
participant Pending as "PendingRequests"
participant Serializer as "JsonMessageSerializer"
participant Stream as "NetworkStream"
Client->>Client : "Generate MessageId"
Client->>Client : "Create RequestMessage"
Client->>Serializer : "SerializeRequest"
Client->>Stream : "Write header(Request) + payload"
Client->>Pending : "Add TaskCompletionSource(MessageId)"
Note over Client : "Await response with timeout"
Stream-->>Client : "Receive Response frame"
Client->>Serializer : "DeserializeResponse"
Client->>Pending : "TryRemove(MessageId)"
Client-->>Client : "Complete Task with ResponseMessage"
```

**Diagram sources**
- [TcpRpcClient.cs:132-189](file://TcpRpc/Client/TcpRpcClient.cs#L132-L189)
- [JsonMessageSerializer.cs:14-20](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L14-L20)
- [MessageHeader.cs:13-20](file://TcpRpc/Message/MessageHeader.cs#L13-L20)

**Section sources**
- [TcpRpcClient.cs:132-189](file://TcpRpc/Client/TcpRpcClient.cs#L132-L189)
- [RpcClientState.cs:1-11](file://TcpRpc/Client/RpcClientState.cs#L1-L11)

### Server-Side Handler Execution and Response Generation
- Handler registration:
  - RpcServer.RegisterHandler associates a Method name with a RequestHandler delegate or a typed handler that deserializes request Data and returns a strongly typed result.
- Request processing:
  - RpcServer reads MessageHeader, validates ContentLength limits, deserializes RequestMessage, and invokes the registered handler.
- Response generation:
  - On success, RpcServer serializes the result to UTF-8 bytes and sends a Response frame.
  - On failure, RpcServer sends an Error frame with the error message.
- Ping/Pong:
  - RpcServer responds to Ping frames with Pong frames.

```mermaid
flowchart TD
Start(["Receive Request Frame"]) --> ReadHeader["Read MessageHeader"]
ReadHeader --> ValidateSize{"ContentLength <= 100MB?"}
ValidateSize --> |No| SendError["Send Error frame"]
ValidateSize --> |Yes| ReadPayload["Read payload bytes"]
ReadPayload --> Deserialize["Deserialize RequestMessage"]
Deserialize --> Lookup{"Method found?"}
Lookup --> |No| BuildNotFound["Build Response(Success=false, Error='Method not found')"]
Lookup --> |Yes| Invoke["Invoke handler"]
Invoke --> HandlerResult{"Handler throws?"}
HandlerResult --> |Yes| BuildError["Build Response(Success=false, Error=message)"]
HandlerResult --> |No| Serialize["Serialize result to UTF-8 bytes"]
Serialize --> SendResponse["Send Response frame"]
BuildNotFound --> SendResponse
BuildError --> SendResponse
SendError --> End(["Done"])
SendResponse --> End
```

**Diagram sources**
- [TcpRpcServer.cs:228-376](file://TcpRpc/Server/TcpRpcServer.cs#L228-L376)
- [RequestHandler.cs:1-5](file://TcpRpc/Server/RequestHandler.cs#L1-L5)
- [JsonMessageSerializer.cs:51-84](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L51-L84)

**Section sources**
- [TcpRpcServer.cs:50-99](file://TcpRpc/Server/TcpRpcServer.cs#L50-L99)
- [TcpRpcServer.cs:318-376](file://TcpRpc/Server/TcpRpcServer.cs#L318-L376)
- [RequestHandler.cs:1-5](file://TcpRpc/Server/RequestHandler.cs#L1-L5)

### Correlation Mechanism Between Requests and Responses
- MessageId:
  - RpcClient auto-increments MessageId per request and stores a TaskCompletionSource keyed by MessageId.
  - RpcServer preserves MessageId in ResponseMessage so RpcClient can correlate incoming responses to pending requests.
- Error propagation:
  - RpcServer can send Error frames for malformed messages or oversized payloads; RpcClient treats these as faults and removes the pending request.

```mermaid
sequenceDiagram
participant Client as "RpcClient"
participant Pending as "PendingRequests"
participant Server as "RpcServer"
Client->>Client : "MessageId++"
Client->>Pending : "Add(MessageId -> TCS)"
Client->>Server : "Send Request(MessageId)"
Server->>Server : "Preserve MessageId"
Server-->>Client : "Send Response(MessageId)"
Client->>Pending : "TryRemove(MessageId)"
Client-->>Client : "Complete TCS with Response"
```

**Diagram sources**
- [TcpRpcClient.cs:139-143](file://TcpRpc/Client/TcpRpcClient.cs#L139-L143)
- [TcpRpcClient.cs:263-267](file://TcpRpc/Client/TcpRpcClient.cs#L263-L267)
- [TcpRpcServer.cs:343-359](file://TcpRpc/Server/TcpRpcServer.cs#L343-L359)

**Section sources**
- [TcpRpcClient.cs:139-143](file://TcpRpc/Client/TcpRpcClient.cs#L139-L143)
- [TcpRpcClient.cs:263-267](file://TcpRpc/Client/TcpRpcClient.cs#L263-L267)
- [TcpRpcServer.cs:343-359](file://TcpRpc/Server/TcpRpcServer.cs#L343-L359)

### Timeout Handling and Cleanup Procedures
- Timeouts:
  - RpcClient.SendRequestAsync uses a linked cancellation token with a configurable timeout; on cancellation, the pending request is removed and a TimeoutException is thrown.
- Cleanup:
  - RpcClient.DisconnectAsync cancels receive loop, closes streams, and completes all pending requests with exceptions.
  - RpcServer.StopAsync cancels all operations, stops accepting connections, and waits for client tasks to finish.
  - Both Dispose methods ensure resources are released.

**Section sources**
- [TcpRpcClient.cs:162-188](file://TcpRpc/Client/TcpRpcClient.cs#L162-L188)
- [TcpRpcClient.cs:85-130](file://TcpRpc/Client/TcpRpcClient.cs#L85-L130)
- [TcpRpcServer.cs:175-226](file://TcpRpc/Server/TcpRpcServer.cs#L175-L226)

## Dependency Analysis
The following diagram shows how components depend on each other to implement the request-response protocol.

```mermaid
graph LR
RM["RequestMessage"] --> JMS["JsonMessageSerializer"]
RSM["ResponseMessage"] --> JMS
JMS --> IS["IMessageSerializer"]
MH["MessageHeader"] --> TCPCL["RpcClient"]
MH --> TCPS["RpcServer"]
MT["MessageType"] --> MH
TCPCL --> MH
TCPCL --> JMS
TCPS --> MH
TCPS --> JMS
TCPS --> RH["RequestHandler"]
```

**Diagram sources**
- [RequestMessage.cs:1-8](file://TcpRpc/Message/RequestMessage.cs#L1-L8)
- [ResponseMessage.cs:1-9](file://TcpRpc/Message/ResponseMessage.cs#L1-L9)
- [JsonMessageSerializer.cs:1-85](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L1-L85)
- [IMessageSerializer.cs:1-13](file://TcpRpc/Formatter/IMessageSerializer.cs#L1-L13)
- [MessageHeader.cs:1-34](file://TcpRpc/Message/MessageHeader.cs#L1-L34)
- [MessageType.cs:1-10](file://TcpRpc/Message/MessageType.cs#L1-L10)
- [TcpRpcClient.cs:1-405](file://TcpRpc/Client/TcpRpcClient.cs#L1-L405)
- [TcpRpcServer.cs:1-432](file://TcpRpc/Server/TcpRpcServer.cs#L1-L432)
- [RequestHandler.cs:1-5](file://TcpRpc/Server/RequestHandler.cs#L1-L5)

**Section sources**
- [IMessageSerializer.cs:1-13](file://TcpRpc/Formatter/IMessageSerializer.cs#L1-L13)
- [JsonMessageSerializer.cs:1-85](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L1-L85)
- [TcpRpcClient.cs:1-405](file://TcpRpc/Client/TcpRpcClient.cs#L1-L405)
- [TcpRpcServer.cs:1-432](file://TcpRpc/Server/TcpRpcServer.cs#L1-L432)

## Performance Considerations
- Payload size limits:
  - Client enforces a 10 MB limit when reading content; oversized messages cause an InvalidDataException and connection handling is adjusted accordingly.
  - Server enforces a 100 MB limit for incoming messages; oversized messages trigger an Error frame.
- Large payloads:
  - Prefer streaming or chunked transfer strategies if payloads exceed these limits. Consider breaking large data into smaller requests or using separate channels for large transfers.
- JSON formatting:
  - JsonMessageSerializer uses camelCase and compact output; avoid unnecessary whitespace or extra fields to reduce payload size.
- Concurrency and throughput:
  - RpcClient uses a semaphore to serialize writes and a concurrent dictionary for pending requests; ensure handlers execute efficiently to minimize queue buildup.
- Memory usage:
  - Avoid retaining large byte arrays unnecessarily; dispose streams and buffers promptly after sending/receiving.

**Section sources**
- [TcpRpcClient.cs:215-218](file://TcpRpc/Client/TcpRpcClient.cs#L215-L218)
- [TcpRpcServer.cs:264-270](file://TcpRpc/Server/TcpRpcServer.cs#L264-L270)

## Troubleshooting Guide
- Invalid request/response deserialization:
  - JsonMessageSerializer throws when data is null, empty, or fails JSON parsing. Verify that the payload is a valid UTF-8 JSON representation of the expected model.
- Invalid MessageId:
  - Deserialization validates MessageId must be positive; ensure MessageId is generated and preserved correctly.
- Missing Method:
  - RpcServer returns Success=false with an error indicating the method was not found; confirm handler registration and Method name.
- Handler exceptions:
  - RpcServer wraps handler exceptions in a Response with Success=false and the exception message; inspect Error for diagnostics.
- Oversized messages:
  - Client and Server enforce size limits; adjust payload size or implement chunking.
- Timeouts:
  - RpcClient throws TimeoutException when the server does not respond within the configured timeout; increase timeout or investigate server responsiveness.
- Connection loss:
  - RpcClient completes pending requests with exceptions when the connection is lost; reconnect and resend as needed.

**Section sources**
- [JsonMessageSerializer.cs:22-49](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L22-L49)
- [JsonMessageSerializer.cs:59-84](file://TcpRpc/Formatter/JsonMessageSerializer.cs#L59-L84)
- [TcpRpcServer.cs:343-359](file://TcpRpc/Server/TcpRpcServer.cs#L343-L359)
- [TcpRpcClient.cs:179-188](file://TcpRpc/Client/TcpRpcClient.cs#L179-L188)

## Conclusion
RequestMessage and ResponseMessage are the core payload structures in TcpRpc, designed for efficient JSON serialization and robust correlation via MessageId. The JsonMessageSerializer enforces structural validation, while RpcClient and RpcServer coordinate request/response lifecycles, timeouts, and cleanup. Adhering to payload size limits and efficient handler execution ensures reliable performance for both small and large payloads.
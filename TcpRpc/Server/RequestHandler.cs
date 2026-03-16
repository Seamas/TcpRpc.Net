using TcpRpc.Message;

namespace TcpRpc.Server;

public delegate Task<ResponseMessage> RequestHandler(RequestMessage request);
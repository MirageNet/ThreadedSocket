using Mirage.SocketLayer;

namespace Mirage.ThreadedSocket
{
    public class ThreadedSocketFactory : SocketFactory
    {
        public SocketFactory Inner;
        public int BufferSize;

        public override ISocket CreateClientSocket()
        {
            return new MultiThreadSocket(Inner.CreateClientSocket(), BufferSize);
        }

        public override ISocket CreateServerSocket()
        {
            return new MultiThreadSocket(Inner.CreateServerSocket(), BufferSize);
        }

        public override IEndPoint GetBindEndPoint()
        {
            return Inner.GetBindEndPoint();
        }

        public override IEndPoint GetConnectEndPoint(string address = null, ushort? port = null)
        {
            return Inner.GetConnectEndPoint(address, port);
        }
    }
}

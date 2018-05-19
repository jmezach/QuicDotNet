using System;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal
{
    public class QuicReceiver : IDisposable
    {
        private readonly Socket _socket;
        private readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();
        private readonly QuicAwaitable _awaitable;

        public QuicReceiver(Socket socket, PipeScheduler scheduler)
        {
            _socket = socket;
            _awaitable = new QuicAwaitable(scheduler);
            _eventArgs.UserToken = _awaitable;
            _eventArgs.Completed += (_, e) => ((QuicAwaitable)e.UserToken).Complete(e.BytesTransferred, e.SocketError);
        }

        public QuicAwaitable ReceiveAsync(Memory<byte> buffer)
        {
#if NETCOREAPP2_1
            _eventArgs.SetBuffer(buffer);
#else
            var segment = buffer.ToArray();

            _eventArgs.SetBuffer(segment, 0, segment.Length);
#endif
            if (!_socket.ReceiveAsync(_eventArgs))
            {
                _awaitable.Complete(_eventArgs.BytesTransferred, _eventArgs.SocketError);
            }

            return _awaitable;
        }

        public void Dispose()
        {
            _eventArgs.Dispose();
        }
    }
}
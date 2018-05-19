using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal
{
    public class QuicSender : IDisposable
    {
        private readonly Socket _socket;
        private readonly QuicAwaitable _awaitable;
        private readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();

        private List<ArraySegment<byte>> _bufferList;

        public QuicSender(Socket socket, PipeScheduler scheduler)
        {
            _socket = socket;
            _awaitable = new QuicAwaitable(scheduler);
            _eventArgs.UserToken = _awaitable;
            _eventArgs.Completed += (_, e) => ((QuicAwaitable)e.UserToken).Complete(e.BytesTransferred, e.SocketError);
        }
        
        public QuicAwaitable SendAsync(ReadOnlySequence<byte> buffers)
        {
            if (buffers.IsSingleSegment)
            {
                return SendAsync(buffers.First);
            }

#if NETCOREAPP2_1
            if (!_eventArgs.MemoryBuffer.Equals(Memory<byte>.Empty))
#else
            if (_eventArgs.Buffer != null)
#endif
            {
                _eventArgs.SetBuffer(null, 0, 0);
            }

            _eventArgs.BufferList = GetBufferList(buffers);

            if (!_socket.SendAsync(_eventArgs))
            {
                _awaitable.Complete(_eventArgs.BytesTransferred, _eventArgs.SocketError);
            }

            return _awaitable;
        }

        private QuicAwaitable SendAsync(ReadOnlyMemory<byte> memory)
        {
            // The BufferList getter is much less expensive then the setter.
            if (_eventArgs.BufferList != null)
            {
                _eventArgs.BufferList = null;
            }

#if NETCOREAPP2_1
            _eventArgs.SetBuffer(MemoryMarshal.AsMemory(memory));
#else
            var segment = memory.ToArray();

            _eventArgs.SetBuffer(segment, 0, segment.Length);
#endif
            if (!_socket.SendAsync(_eventArgs))
            {
                _awaitable.Complete(_eventArgs.BytesTransferred, _eventArgs.SocketError);
            }

            return _awaitable;
        }

        private List<ArraySegment<byte>> GetBufferList(ReadOnlySequence<byte> buffer)
        {
            Debug.Assert(!buffer.IsEmpty);
            Debug.Assert(!buffer.IsSingleSegment);

            if (_bufferList == null)
            {
                _bufferList = new List<ArraySegment<byte>>();
            }
            else
            {
                // Buffers are pooled, so it's OK to root them until the next multi-buffer write.
                _bufferList.Clear();
            }

            foreach (var b in buffer)
            {
                _bufferList.Add(new ArraySegment<byte>(b.ToArray()));
            }

            return _bufferList;
        }

        public void Dispose()
        {
            _eventArgs.Dispose();
        }
    }
}
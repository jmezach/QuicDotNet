using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal
{
    internal sealed class QuicConnection : TransportConnection
    {
        internal QuicConnection(Socket socket, MemoryPool<byte> memoryPool, PipeScheduler scheduler, ILogger logger)
        {
            
        }

        public async Task StartAsync(IConnectionDispatcher dispatcher)
        {
            System.Console.WriteLine("Testing");
        }

        public override string ConnectionId { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public override IFeatureCollection Features => throw new System.NotImplementedException();

        public override IDictionary<object, object> Items { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public override IDuplexPipe Transport { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    }
}
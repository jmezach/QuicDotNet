using System;
using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal;
using Microsoft.Extensions.Logging;
using QuicDotNet.Packets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic
{
    internal sealed class QuicTransport : ITransport
    {
        private static readonly PipeScheduler[] ThreadPoolSchedulerArray = new PipeScheduler[] { PipeScheduler.ThreadPool };
        private readonly MemoryPool<byte> _memoryPool = KestrelMemoryPool.Create();
        private readonly IEndPointInformation _endPointInformation;
        private readonly IConnectionDispatcher _dispatcher;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly ILogger _logger;
        private readonly int _numSchedulers;
        private readonly PipeScheduler[] _schedulers;
        private Socket _listenSocket;
        private Task _listenTask;
        private Exception _listenException;
        private volatile bool _unbinding;

        internal QuicTransport(
            IEndPointInformation endPointInformation,
            IConnectionDispatcher dispatcher,
            IApplicationLifetime applicationLifetime,
            int ioQueueCount,
            ILogger logger)
        {
            _endPointInformation = endPointInformation;
            _dispatcher = dispatcher;
            _applicationLifetime = applicationLifetime;
            _logger = logger;

            if (ioQueueCount > 0)
            {
                _numSchedulers = ioQueueCount;
                _schedulers = new IOQueue[_numSchedulers];

                for (var i = 0; i < _numSchedulers; i++)
                {
                    _schedulers[i] = new IOQueue();
                }
            }
            else
            {
                _numSchedulers = ThreadPoolSchedulerArray.Length;
                _schedulers = ThreadPoolSchedulerArray;
            }
        }

        public Task BindAsync()
        {
            if (_listenSocket != null)
            {
                throw new InvalidOperationException("Transport already bound");
            }

            IPEndPoint endPoint = _endPointInformation.IPEndPoint;

            var listenSocket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            //EnableRebinding(listenSocket);

            // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
            if (endPoint.Address == IPAddress.IPv6Any)
            {
                listenSocket.DualMode = true;
            }

            try
            {
                listenSocket.Bind(endPoint);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                throw new AddressInUseException(e.Message, e);
            }

            // If requested port was "0", replace with assigned dynamic port.
            if (_endPointInformation.IPEndPoint.Port == 0)
            {
                _endPointInformation.IPEndPoint = (IPEndPoint)listenSocket.LocalEndPoint;
            }

            //listenSocket.Listen(512);

            _listenSocket = listenSocket;

            _listenTask = Task.Run(() => RunAcceptLoopAsync());

            return Task.CompletedTask;
        }

        public async Task UnbindAsync()
        {
            if (_listenSocket != null)
            {
                _unbinding = true;
                _listenSocket.Dispose();

                Debug.Assert(_listenTask != null);
                await _listenTask.ConfigureAwait(false);

                _unbinding = false;
                _listenSocket = null;
                _listenTask = null;

                if (_listenException != null)
                {
                    var exInfo = ExceptionDispatchInfo.Capture(_listenException);
                    _listenException = null;
                    exInfo.Throw();
                }
            }
        }

        public Task StopAsync()
        {
            _memoryPool.Dispose();
            return Task.CompletedTask;
        }

        private async Task RunAcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    for (var schedulerIndex = 0; schedulerIndex < _numSchedulers;  schedulerIndex++)
                    {
                        try
                        {
                            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[4096]);
                            int receivedBytes = await _listenSocket.ReceiveAsync(buffer, SocketFlags.None);
                            if (receivedBytes > 0)
                            {
                                byte publicFlags = buffer.Array[0];
                                bool versionPresent = (publicFlags & 0x01) != 0;
                                bool resetPresent = (publicFlags & 0x02) != 0;

                                System.Console.WriteLine("PUBLIC_FLAGS_VERSION:" + versionPresent);
                                System.Console.WriteLine("PUBLIC_FLAGS_RESET:" + resetPresent);

                                int next = 1;

                                UInt64 connectionID = 0;
                                if ((publicFlags & 0x0C) != 0) {
                                    System.Console.WriteLine("8 byte connectionID");
                                    connectionID = BitConverter.ToUInt64(buffer.AsSpan().Slice(next, 8).ToArray(), 0);
                                    next += 8;
                                } else if ((publicFlags & 0x08) != 0) {
                                    System.Console.WriteLine("4 byte connectionID");
                                    connectionID = BitConverter.ToUInt32(buffer.AsSpan().Slice(next, 4).ToArray(), 0);
                                    next += 4;
                                } else if ((publicFlags & 0x04) != 0) {
                                    System.Console.WriteLine("2 byte connectionID");
                                    connectionID = BitConverter.ToUInt16(buffer.AsSpan().Slice(next, 4).ToArray(), 0);
                                    next += 2;
                                }

                                string quicVersion = Encoding.ASCII.GetString(buffer.AsSpan().Slice(next, 4).ToArray(), 0, 4);
                                next += 4;

                                UInt64 packetNumber = 0;
                                if ((publicFlags & 0x30) != 0) {
                                    System.Console.WriteLine("6 bytes packetNumber");
                                    packetNumber = BitConverter.ToUInt64(buffer.AsSpan().Slice(next, 8).ToArray(), 0);
                                } else if ((publicFlags & 0x20) != 0) {
                                    System.Console.WriteLine("4 bytes packetNumber");
                                    packetNumber = BitConverter.ToUInt32(buffer.AsSpan().Slice(next, 4).ToArray(), 0);
                                } else if ((publicFlags & 0x10) != 0) {
                                    System.Console.WriteLine("2 bytes packetNumber");
                                    packetNumber = BitConverter.ToUInt16(buffer.AsSpan().Slice(next, 2).ToArray(), 0);
                                } else {
                                    System.Console.WriteLine("1 byte packetNumber");
                                    packetNumber = buffer.AsSpan().Slice(next, 1).ToArray()[0];
                                }

                                System.Console.WriteLine("ConnectionId: " + connectionID);
                                System.Console.WriteLine("QuicVersion: " + quicVersion);
                                System.Console.WriteLine("PacketNumber: " + packetNumber);
                            }
                            

                            var connection = new QuicConnection(_listenSocket, _memoryPool, _schedulers[schedulerIndex], _logger);
                            _ = connection.StartAsync(_dispatcher);
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            // REVIEW: Should there be a separate log message for a connection reset this early?
                            _logger.LogDebug($"Connection Reset: connectionId: ({null})");
                        }
                        catch (SocketException ex) when (!_unbinding)
                        {
                            _logger.LogError($"Connection Error: connectionId: ({null})", ex);

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_unbinding)
                {
                    // Means we must be unbinding. Eat the exception.
                }
                else
                {
                    _logger.LogCritical(ex, $"Unexpected exeption in {nameof(QuicTransport)}.{nameof(RunAcceptLoopAsync)}.");
                    _listenException = ex;

                    // Request shutdown so we can rethrow this exception
                    // in Stop which should be observable.
                    _applicationLifetime.StopApplication();
                }
            }
        }
    }
}
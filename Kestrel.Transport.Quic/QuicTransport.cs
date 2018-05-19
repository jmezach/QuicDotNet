using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using Kestrel.Transport.Quic.Internal.Headers;
using Kestrel.Transport.Quic.Internal.Packets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal;
using Microsoft.Extensions.Logging;

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
        private Dictionary<UInt64, QuicConnection> _connections = new Dictionary<UInt64, QuicConnection>();
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
                            IPEndPoint sender = new IPEndPoint(_listenSocket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                            SocketReceiveFromResult receiveResult = await _listenSocket.ReceiveFromAsync(buffer, SocketFlags.None, sender);
                            if (receiveResult.ReceivedBytes > 0)
                            {
                                AbstractPacketBase packet = AbstractPacketBase.Parse(buffer.AsSpan());
                                System.Console.WriteLine($"rcv {receiveResult.RemoteEndPoint} " + packet.ToString());

                                //var response = new RegularPacket(connectionID, 2, null);
                                //await _listenSocket.SendToAsync(new ArraySegment<byte>(response.PadAndNullEncrypt()), SocketFlags.None, receiveResult.RemoteEndPoint);

                                /* 
                                if (_connections.TryGetValue(connectionID, out QuicConnection connection))
                                {
                                    await connection.Input.WriteAsync(buffer);
                                }
                                else
                                {
                                    connection = new QuicConnection(connectionID.ToString(), _listenSocket, _memoryPool, _schedulers[schedulerIndex], _logger);
                                    _connections.Add(connectionID, connection);
                                    await connection.StartAsync(_dispatcher);
                                }
                                */
                            }
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            // REVIEW: Should there be a separate log message for a connection reset this early?
                            _logger.LogDebug($"Connection Reset: connectionId: ({null})");
                        }
                        catch (SocketException ex) when (!_unbinding)
                        {
                            _logger.LogError(ex, $"Connection Error: connectionId: ({null})");

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
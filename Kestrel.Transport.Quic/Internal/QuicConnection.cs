using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal
{
    internal sealed class QuicConnection : TransportConnection
    {
        private static readonly int MinAllocBufferSize = KestrelMemoryPool.MinimumSegmentSize / 2;
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        private readonly string _connectionId;
        private readonly Socket _socket;
        private readonly QuicReceiver _receiver;
        private readonly QuicSender _sender;
        private readonly PipeScheduler _scheduler;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _connectionClosedTokenSource = new CancellationTokenSource();
        private volatile bool _aborted;
        private readonly object _shutdownLock = new object();
        private long _totalBytesWritten;

        internal QuicConnection(string connectionId, Socket socket, MemoryPool<byte> memoryPool, PipeScheduler scheduler, ILogger logger)
        {
            Debug.Assert(socket != null);
            Debug.Assert(memoryPool != null);
            Debug.Assert(logger != null);

            _socket = socket;
            MemoryPool = memoryPool;
            _scheduler = scheduler;
            _logger = logger;

            var localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
            var remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;

            LocalAddress = localEndPoint.Address;
            LocalPort = localEndPoint.Port;

            RemoteAddress = remoteEndPoint.Address;
            RemotePort = remoteEndPoint.Port;

            ConnectionClosed = _connectionClosedTokenSource.Token;

            // On *nix platforms, Sockets already dispatches to the ThreadPool.
            var awaiterScheduler = IsWindows ? _scheduler : PipeScheduler.Inline;

            _receiver = new QuicReceiver(_socket, awaiterScheduler);
            _sender = new QuicSender(_socket, awaiterScheduler);
        }

        public override string ConnectionId { get { return _connectionId; } }

        public override MemoryPool<byte> MemoryPool { get; }

        public async Task StartAsync(IConnectionDispatcher dispatcher)
        {
            Exception sendError = null;
            try
            {
                dispatcher.OnConnection(this);

                // Spawn send and receive logic
                Task receiveTask = DoReceive();
                Task<Exception> sendTask = DoSend();

                // If the sending task completes then close the receive
                // We don't need to do this in the other direction because the kestrel
                // will trigger the output closing once the input is complete.
                if (await Task.WhenAny(receiveTask, sendTask) == sendTask)
                {
                    // Tell the reader it's being aborted
                    _socket.Dispose();
                }

                // Now wait for both to complete
                await receiveTask;
                sendError = await sendTask;

                // Dispose the socket(should noop if already called)
                _socket.Dispose();
                _receiver.Dispose();
                _sender.Dispose();
                ThreadPool.QueueUserWorkItem(state => ((QuicConnection)state).CancelConnectionClosedToken(), this);
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, $"Unexpected exception in {nameof(QuicConnection)}.{nameof(StartAsync)}.");
            }
            finally
            {
                // Complete the output after disposing the socket
                Output.Complete(sendError);
            }
        }

        private async Task DoReceive()
        {
            Exception error = null;

            try
            {
                await ProcessReceives();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                error = new ConnectionResetException(ex.Message, ex);
                _logger.LogWarning("Connection Reset: " + ConnectionId);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted ||
                                             ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                if (!_aborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                    _logger.LogError(ConnectionId, error);
                }
            }
            catch (ObjectDisposedException)
            {
                if (!_aborted)
                {
                    error = new ConnectionAbortedException();
                    _logger.LogError(ConnectionId, error);
                }
            }
            catch (IOException ex)
            {
                error = ex;
                _logger.LogError(ConnectionId, error);
            }
            catch (Exception ex)
            {
                error = new IOException(ex.Message, ex);
                _logger.LogError(ConnectionId, error);
            }
            finally
            {
                if (_aborted)
                {
                    error = error ?? new ConnectionAbortedException();
                }

                Input.Complete(error);
            }
        }

        private async Task ProcessReceives()
        {
            while (true)
            {
                // Ensure we have some reasonable amount of buffer space
                var buffer = Input.GetMemory(MinAllocBufferSize);

                var bytesReceived = await _receiver.ReceiveAsync(buffer);

                if (bytesReceived == 0)
                {
                    // FIN
                    _logger.LogWarning("FIN: " + ConnectionId);
                    break;
                }

                Input.Advance(bytesReceived);

                var flushTask = Input.FlushAsync();

                if (!flushTask.IsCompleted)
                {
                    _logger.LogWarning("PAUSE: " + ConnectionId);

                    await flushTask;

                    _logger.LogWarning("RESUME: " + ConnectionId);
                }

                var result = flushTask.GetAwaiter().GetResult();
                if (result.IsCompleted)
                {
                    // Pipe consumer is shut down, do we stop writing
                    break;
                }
            }
        }

        private async Task<Exception> DoSend()
        {
            Exception error = null;

            try
            {
                await ProcessSends();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                error = null;
            }
            catch (ObjectDisposedException)
            {
                error = null;
            }
            catch (IOException ex)
            {
                error = ex;
            }
            catch (Exception ex)
            {
                error = new IOException(ex.Message, ex);
            }

            Shutdown();

            return error;
        }

        private async Task ProcessSends()
        {
            while (true)
            {
                // Wait for data to write from the pipe producer
                var result = await Output.ReadAsync();
                var buffer = result.Buffer;

                if (result.IsCanceled)
                {
                    break;
                }

                var end = buffer.End;
                var isCompleted = result.IsCompleted;
                if (!buffer.IsEmpty)
                {
                    await _sender.SendAsync(buffer);
                }

                // This is not interlocked because there could be a concurrent writer.
                // Instead it's to prevent read tearing on 32-bit systems.
                Interlocked.Add(ref _totalBytesWritten, buffer.Length);

                Output.AdvanceTo(end);

                if (isCompleted)
                {
                    break;
                }
            }
        }

        private void Shutdown()
        {
            lock (_shutdownLock)
            {
                if (!_aborted)
                {
                    // Make sure to close the connection only after the _aborted flag is set.
                    // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
                    // a BadHttpRequestException is thrown instead of a TaskCanceledException.
                    _aborted = true;
                    _logger.LogWarning("WRITE FIN:" + ConnectionId);

                    // Try to gracefully close the socket even for aborts to match libuv behavior.
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
        }

        private void CancelConnectionClosedToken()
        {
            try
            {
                _connectionClosedTokenSource.Cancel();
                _connectionClosedTokenSource.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(0, ex, $"Unexpected exception in {nameof(QuicConnection)}.{nameof(CancelConnectionClosedToken)}.");
            }
        }
    }
}
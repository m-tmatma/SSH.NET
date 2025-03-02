﻿#nullable enable
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;

namespace Renci.SshNet
{
    /// <summary>
    /// Serves as base class for client implementations, provides common client functionality.
    /// </summary>
    public abstract class BaseClient : IBaseClient
    {
        /// <summary>
        /// Holds value indicating whether the connection info is owned by this client.
        /// </summary>
        private readonly bool _ownsConnectionInfo;

        private readonly ILogger _logger;
        private readonly IServiceFactory _serviceFactory;
        private readonly object _keepAliveLock = new object();
        private TimeSpan _keepAliveInterval;
        private Timer? _keepAliveTimer;
        private ConnectionInfo _connectionInfo;
        private bool _isDisposed;

        /// <summary>
        /// Gets the current session.
        /// </summary>
        /// <value>
        /// The current session.
        /// </value>
        internal ISession? Session { get; private set; }

        /// <summary>
        /// Gets the factory for creating new services.
        /// </summary>
        /// <value>
        /// The factory for creating new services.
        /// </value>
        internal IServiceFactory ServiceFactory
        {
            get { return _serviceFactory; }
        }

        /// <summary>
        /// Gets the connection info.
        /// </summary>
        /// <value>
        /// The connection info.
        /// </value>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
        public ConnectionInfo ConnectionInfo
        {
            get
            {
                CheckDisposed();
                return _connectionInfo;
            }
            private set
            {
                _connectionInfo = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this client is connected to the server.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if this client is connected; otherwise, <see langword="false"/>.
        /// </value>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
        public virtual bool IsConnected
        {
            get
            {
                CheckDisposed();

                return IsSessionConnected();
            }
        }

        /// <summary>
        /// Gets or sets the keep-alive interval.
        /// </summary>
        /// <value>
        /// The keep-alive interval. Specify negative one (-1) milliseconds to disable the
        /// keep-alive. This is the default value.
        /// </value>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
        public TimeSpan KeepAliveInterval
        {
            get
            {
                CheckDisposed();
                return _keepAliveInterval;
            }
            set
            {
                CheckDisposed();

                value.EnsureValidTimeout(nameof(KeepAliveInterval));

                if (value == _keepAliveInterval)
                {
                    return;
                }

                if (value == Timeout.InfiniteTimeSpan)
                {
                    // stop the timer when the value is -1 milliseconds
                    StopKeepAliveTimer();
                }
                else
                {
                    if (_keepAliveTimer != null)
                    {
                        // change the due time and interval of the timer if has already
                        // been created (which means the client is connected)
                        _ = _keepAliveTimer.Change(value, value);
                    }
                    else if (IsSessionConnected())
                    {
                        // if timer has not yet been created and the client is already connected,
                        // then we need to create the timer now
                        //
                        // this means that - before connecting - the keep-alive interval was set to
                        // negative one (-1) and as such we did not create the timer
                        _keepAliveTimer = CreateKeepAliveTimer(value, value);
                    }

                    // note that if the client is not yet connected, then the timer will be created with the
                    // new interval when Connect() is invoked
                }

                _keepAliveInterval = value;
            }
        }

        /// <summary>
        /// Occurs when an error occurred.
        /// </summary>
        public event EventHandler<ExceptionEventArgs>? ErrorOccurred;

        /// <summary>
        /// Occurs when host key received.
        /// </summary>
        public event EventHandler<HostKeyEventArgs>? HostKeyReceived;

        /// <summary>
        /// Occurs when server identification received.
        /// </summary>
        public event EventHandler<SshIdentificationEventArgs>? ServerIdentificationReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseClient"/> class.
        /// </summary>
        /// <param name="connectionInfo">The connection info.</param>
        /// <param name="ownsConnectionInfo">Specified whether this instance owns the connection info.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connectionInfo"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// If <paramref name="ownsConnectionInfo"/> is <see langword="true"/>, then the
        /// connection info will be disposed when this instance is disposed.
        /// </remarks>
        protected BaseClient(ConnectionInfo connectionInfo, bool ownsConnectionInfo)
            : this(connectionInfo, ownsConnectionInfo, new ServiceFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseClient"/> class.
        /// </summary>
        /// <param name="connectionInfo">The connection info.</param>
        /// <param name="ownsConnectionInfo">Specified whether this instance owns the connection info.</param>
        /// <param name="serviceFactory">The factory to use for creating new services.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connectionInfo"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="serviceFactory"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// If <paramref name="ownsConnectionInfo"/> is <see langword="true"/>, then the
        /// connection info will be disposed when this instance is disposed.
        /// </remarks>
        private protected BaseClient(ConnectionInfo connectionInfo, bool ownsConnectionInfo, IServiceFactory serviceFactory)
        {
            ThrowHelper.ThrowIfNull(connectionInfo);
            ThrowHelper.ThrowIfNull(serviceFactory);

            _connectionInfo = connectionInfo;
            _ownsConnectionInfo = ownsConnectionInfo;
            _serviceFactory = serviceFactory;
            _logger = SshNetLoggingConfiguration.LoggerFactory.CreateLogger(GetType());
            _keepAliveInterval = Timeout.InfiniteTimeSpan;
        }

        /// <summary>
        /// Connects client to the server.
        /// </summary>
        /// <exception cref="InvalidOperationException">The client is already connected.</exception>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
        /// <exception cref="SocketException">Socket connection to the SSH server or proxy server could not be established, or an error occurred while resolving the hostname.</exception>
        /// <exception cref="SshConnectionException">SSH session could not be established.</exception>
        /// <exception cref="SshAuthenticationException">Authentication of SSH session failed.</exception>
        /// <exception cref="ProxyException">Failed to establish proxy connection.</exception>
        public void Connect()
        {
            CheckDisposed();

            // TODO (see issue #1758):
            // we're not stopping the keep-alive timer and disposing the session here
            //
            // we could do this but there would still be side effects as concrete
            // implementations may still hang on to the original session
            //
            // therefore it would be better to actually invoke the Disconnect method
            // (and then the Dispose on the session) but even that would have side effects
            // eg. it would remove all forwarded ports from SshClient
            //
            // I think we should modify our concrete clients to better deal with a
            // disconnect. In case of SshClient this would mean not removing the
            // forwarded ports on disconnect (but only on dispose ?) and link a
            // forwarded port with a client instead of with a session
            //
            // To be discussed with Oleg (or whoever is interested)
            if (IsConnected)
            {
                throw new InvalidOperationException("The client is already connected.");
            }

            OnConnecting();

            // The session may already/still be connected here because e.g. in SftpClient, IsConnected also checks the internal SFTP session
            var session = Session;
            if (session is null || !session.IsConnected)
            {
                if (session is not null)
                {
                    DisposeSession(session);
                }

                Session = CreateAndConnectSession();
            }

            try
            {
                // Even though the method we invoke makes you believe otherwise, at this point only
                // the SSH session itself is connected.
                OnConnected();
            }
            catch
            {
                // Only dispose the session as Disconnect() would have side-effects (such as remove forwarded
                // ports in SshClient).
                DisposeSession();
                throw;
            }

            StartKeepAliveTimer();
        }

        /// <summary>
        /// Asynchronously connects client to the server.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous connect operation.
        /// </returns>
        /// <exception cref="InvalidOperationException">The client is already connected.</exception>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
        /// <exception cref="SocketException">Socket connection to the SSH server or proxy server could not be established, or an error occurred while resolving the hostname.</exception>
        /// <exception cref="SshConnectionException">SSH session could not be established.</exception>
        /// <exception cref="SshAuthenticationException">Authentication of SSH session failed.</exception>
        /// <exception cref="ProxyException">Failed to establish proxy connection.</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            CheckDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            // TODO (see issue #1758):
            // we're not stopping the keep-alive timer and disposing the session here
            //
            // we could do this but there would still be side effects as concrete
            // implementations may still hang on to the original session
            //
            // therefore it would be better to actually invoke the Disconnect method
            // (and then the Dispose on the session) but even that would have side effects
            // eg. it would remove all forwarded ports from SshClient
            //
            // I think we should modify our concrete clients to better deal with a
            // disconnect. In case of SshClient this would mean not removing the
            // forwarded ports on disconnect (but only on dispose ?) and link a
            // forwarded port with a client instead of with a session
            //
            // To be discussed with Oleg (or whoever is interested)
            if (IsConnected)
            {
                throw new InvalidOperationException("The client is already connected.");
            }

            OnConnecting();

            // The session may already/still be connected here because e.g. in SftpClient, IsConnected also checks the internal SFTP session
            var session = Session;
            if (session is null || !session.IsConnected)
            {
                if (session is not null)
                {
                    DisposeSession(session);
                }

                using var timeoutCancellationTokenSource = new CancellationTokenSource(ConnectionInfo.Timeout);
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellationTokenSource.Token);

                try
                {
                    Session = await CreateAndConnectSessionAsync(linkedCancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (timeoutCancellationTokenSource.IsCancellationRequested)
                {
                    throw new SshOperationTimeoutException("Connection has timed out.", ex);
                }
            }

            try
            {
                // Even though the method we invoke makes you believe otherwise, at this point only
                // the SSH session itself is connected.
                OnConnected();
            }
            catch
            {
                // Only dispose the session as Disconnect() would have side-effects (such as remove forwarded
                // ports in SshClient).
                DisposeSession();
                throw;
            }

            StartKeepAliveTimer();
        }

        /// <summary>
        /// Disconnects client from the server.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
        public void Disconnect()
        {
            _logger.LogInformation("Disconnecting client.");

            CheckDisposed();

            OnDisconnecting();

            // stop sending keep-alive messages before we close the session
            StopKeepAliveTimer();

            // dispose the SSH session
            DisposeSession();

            OnDisconnected();
        }

        /// <summary>
        /// Sends a keep-alive message to the server.
        /// </summary>
        /// <remarks>
        /// Use <see cref="KeepAliveInterval"/> to configure the client to send a keep-alive at regular
        /// intervals.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The method was called after the client was disposed.</exception>
#pragma warning disable S1133 // Deprecated code should be removed
        [Obsolete("Use KeepAliveInterval to send a keep-alive message at regular intervals.")]
#pragma warning restore S1133 // Deprecated code should be removed
        public void SendKeepAlive()
        {
            CheckDisposed();

            SendKeepAliveMessage();
        }

        /// <summary>
        /// Called when client is connecting to the server.
        /// </summary>
        protected virtual void OnConnecting()
        {
        }

        /// <summary>
        /// Called when client is connected to the server.
        /// </summary>
        protected virtual void OnConnected()
        {
        }

        /// <summary>
        /// Called when client is disconnecting from the server.
        /// </summary>
        protected virtual void OnDisconnecting()
        {
            Session?.OnDisconnecting();
        }

        /// <summary>
        /// Called when client is disconnected from the server.
        /// </summary>
        protected virtual void OnDisconnected()
        {
        }

        private void Session_ErrorOccured(object? sender, ExceptionEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        private void Session_HostKeyReceived(object? sender, HostKeyEventArgs e)
        {
            HostKeyReceived?.Invoke(this, e);
        }

        private void Session_ServerIdentificationReceived(object? sender, SshIdentificationEventArgs e)
        {
            ServerIdentificationReceived?.Invoke(this, e);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _logger.LogDebug("Disposing client.");

                Disconnect();

                if (_ownsConnectionInfo)
                {
                    if (_connectionInfo is IDisposable connectionInfoDisposable)
                    {
                        connectionInfoDisposable.Dispose();
                    }
                }

                _isDisposed = true;
            }
        }

        /// <summary>
        /// Check if the current instance is disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The current instance is disposed.</exception>
        protected void CheckDisposed()
        {
            ThrowHelper.ThrowObjectDisposedIf(_isDisposed, this);
        }

        /// <summary>
        /// Stops the keep-alive timer, and waits until all timer callbacks have been
        /// executed.
        /// </summary>
        private void StopKeepAliveTimer()
        {
            if (_keepAliveTimer is null)
            {
                return;
            }

            _keepAliveTimer.Dispose();
            _keepAliveTimer = null;
        }

        private void SendKeepAliveMessage()
        {
            var session = Session;

            // do nothing if we have disposed or disconnected
            if (session is null)
            {
                return;
            }

            // do not send multiple keep-alive messages concurrently
            if (Monitor.TryEnter(_keepAliveLock))
            {
                try
                {
                    _ = session.TrySendMessage(new IgnoreMessage());
                }
                catch (ObjectDisposedException)
                {
                    // ignore
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending keepalive message");
                }
                finally
                {
                    Monitor.Exit(_keepAliveLock);
                }
            }
        }

        /// <summary>
        /// Starts the keep-alive timer.
        /// </summary>
        /// <remarks>
        /// When <see cref="KeepAliveInterval"/> is negative one (-1) milliseconds, then
        /// the timer will not be started.
        /// </remarks>
        private void StartKeepAliveTimer()
        {
            if (_keepAliveInterval == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            if (_keepAliveTimer != null)
            {
                // timer is already started
                return;
            }

            _keepAliveTimer = CreateKeepAliveTimer(_keepAliveInterval, _keepAliveInterval);
        }

        /// <summary>
        /// Creates a <see cref="Timer"/> with the specified due time and interval.
        /// </summary>
        /// <param name="dueTime">The amount of time to delay before the keep-alive message is first sent. Specify negative one (-1) milliseconds to prevent the timer from starting. Specify zero (0) to start the timer immediately.</param>
        /// <param name="period">The time interval between attempts to send a keep-alive message. Specify negative one (-1) milliseconds to disable periodic signaling.</param>
        /// <returns>
        /// A <see cref="Timer"/> with the specified due time and interval.
        /// </returns>
        private Timer CreateKeepAliveTimer(TimeSpan dueTime, TimeSpan period)
        {
            return new Timer(state => SendKeepAliveMessage(), Session, dueTime, period);
        }

        private ISession CreateAndConnectSession()
        {
            var session = _serviceFactory.CreateSession(ConnectionInfo, _serviceFactory.CreateSocketFactory());
            session.ServerIdentificationReceived += Session_ServerIdentificationReceived;
            session.HostKeyReceived += Session_HostKeyReceived;
            session.ErrorOccured += Session_ErrorOccured;

            try
            {
                session.Connect();
                return session;
            }
            catch
            {
                DisposeSession(session);
                throw;
            }
        }

        private async Task<ISession> CreateAndConnectSessionAsync(CancellationToken cancellationToken)
        {
            var session = _serviceFactory.CreateSession(ConnectionInfo, _serviceFactory.CreateSocketFactory());
            session.ServerIdentificationReceived += Session_ServerIdentificationReceived;
            session.HostKeyReceived += Session_HostKeyReceived;
            session.ErrorOccured += Session_ErrorOccured;

            try
            {
                await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                DisposeSession(session);
                throw;
            }
        }

        private void DisposeSession(ISession session)
        {
            session.ErrorOccured -= Session_ErrorOccured;
            session.HostKeyReceived -= Session_HostKeyReceived;
            session.ServerIdentificationReceived -= Session_ServerIdentificationReceived;
            session.Dispose();
        }

        /// <summary>
        /// Disposes the SSH session, and assigns <see langword="null"/> to <see cref="Session"/>.
        /// </summary>
        private void DisposeSession()
        {
            var session = Session;
            if (session != null)
            {
                Session = null;
                DisposeSession(session);
            }
        }

        /// <summary>
        /// Returns a value indicating whether the SSH session is established.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the SSH session is established; otherwise, <see langword="false"/>.
        /// </returns>
        private bool IsSessionConnected()
        {
            var session = Session;
            return session != null && session.IsConnected;
        }
    }
}

﻿namespace EdgeDB
{
    /// <summary>
    ///     Represents a base edgedb client that can interaction with the EdgeDB database.
    /// </summary>
    public abstract class BaseEdgeDBClient : IEdgeDBQueryable, IAsyncDisposable
    {
        /// <summary>
        ///     Gets whether or not this client has connected to the database and 
        ///     is ready to send queries.
        /// </summary>
        public abstract bool IsConnected { get; }

        /// <summary>
        ///     Gets the client id of this client.
        /// </summary>
        public ulong ClientId { get; }

        internal event Func<BaseEdgeDBClient, ValueTask<bool>> OnDisposed
        {
            add => _onDisposed.Add(value);
            remove => _onDisposed.Remove(value);
        }

        private readonly AsyncEvent<Func<BaseEdgeDBClient, ValueTask<bool>>> _onDisposed = new();

        internal event Func<BaseEdgeDBClient, ValueTask> OnDisconnect
        {
            add => OnDisconnectInternal.Add(value);
            remove => OnDisconnectInternal.Remove(value);
        }

        internal readonly AsyncEvent<Func<BaseEdgeDBClient, ValueTask>> OnDisconnectInternal = new();

        internal event Func<BaseEdgeDBClient, ValueTask> OnConnect
        {
            add => OnConnectInternal.Add(value);
            remove => OnConnectInternal.Remove(value);
        }

        internal readonly AsyncEvent<Func<BaseEdgeDBClient, ValueTask>> OnConnectInternal = new();

        /// <summary>
        ///     Initialized the base client.
        /// </summary>
        /// <param name="clientId">The id of this client.</param>
        public BaseEdgeDBClient(ulong clientId)
        {
            ClientId = clientId;
        }

        /// <summary>
        ///     Disposes or releases this client to the client pool
        /// </summary>
        /// <remarks>
        ///     When overriden in a child class, the child class <b>MUST</b> call base.DisposeAsync 
        ///     and only should dispose if the resulting base call return <see langword="true"/>.
        /// </remarks>
        /// <returns>
        ///     <see langword="true"/> if the client disposed anything; <see langword="false"/> 
        ///     if the client was freed to the client pool.
        /// </returns>
        public virtual async ValueTask<bool> DisposeAsync()
        {
            bool shouldDispose = true;

            if (_onDisposed.HasSubscribers)
            {
                var results = await _onDisposed.InvokeAsync(this).ConfigureAwait(false);
                shouldDispose = results.Any(x => x);
            }

            return shouldDispose;
        }

        /// <summary>
        ///     Disconnects this client from the database.
        /// </summary>
        /// <remarks>
        ///     When overridden, it's <b>strongly</b> recommended to call base.DisconnectAsync
        ///     to ensure the client pool removes this client.
        /// </remarks>
        /// <param name="token">A cancellation token used to cancel the asynchronous operation.</param>
        /// <returns>
        ///     A ValueTask representing the asynchronous disconnect operation.
        /// </returns>
        public virtual ValueTask DisconnectAsync(CancellationToken token = default)
            => OnDisconnectInternal.InvokeAsync(this);

        /// <summary>
        ///     Connects this client to the database.
        /// </summary>
        /// <remarks>
        ///     When overridden, it's <b>strongly</b> recommended to call base.ConnectAsync
        ///     to ensure the client pool adds this client.
        /// </remarks>
        /// <param name="token">A cancellation token used to cancel the asynchronous operation.</param>
        /// <returns>
        ///     A ValueTask representing the asynchronous connect operation.
        /// </returns>
        public virtual ValueTask ConnectAsync(CancellationToken token = default)
            => OnConnectInternal.InvokeAsync(this);

        /// <summary>
        ///     Executes a given query, ignoring any returned data.
        /// </summary>
        /// <param name="query">The query to execute.</param>
        /// <param name="args">Optional collection of arguments within the query.</param>
        /// <param name="token">A cancellation token used to cancel the asynchronous operation.</param>
        /// <returns>
        ///     A task that represents the asynchronous execution operation.
        /// </returns>
        public abstract Task ExecuteAsync(string query, IDictionary<string, object?>? args = null, CancellationToken token = default);

        /// <summary>
        ///     Executes a given query and returns its results.
        /// </summary>
        /// <typeparam name="TResult">The return type of the query.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="args">Optional collection of arguments within the query.</param>
        /// <param name="token">A cancellation token used to cancel the asynchronous operation.</param>
        /// <returns>
        ///      A task that represents the asynchronous execution operation; the tasks result 
        ///      is a <see cref="IReadOnlyCollection{T}"/> containing the 
        ///      <typeparamref name="TResult"/>(s) returned in the query.
        /// </returns>
        public abstract Task<IReadOnlyCollection<TResult?>> QueryAsync<TResult>(string query, IDictionary<string, object?>? args = null, 
            CancellationToken token = default);

        /// <summary>
        ///     Executes a given query and returns the result.
        /// </summary>
        /// <typeparam name="TResult">The return type of the query.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="args">Optional collection of arguments within the query.</param>
        /// <param name="token">A cancellation token used to cancel the asynchronous operation.</param>
        /// <returns>
        ///      A task that represents the asynchronous execution operation; the tasks result 
        ///      is an instance of <typeparamref name="TResult"/>.
        /// </returns>
        public abstract Task<TResult> QueryRequiredSingleAsync<TResult>(string query, IDictionary<string, object?>? args = null, 
            CancellationToken token = default);

        /// <summary>
        ///     Executes a given query and returns the result; or <see langword="null"/> 
        ///     if there was no result.
        /// </summary>
        /// <typeparam name="TResult">The return type of the query.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="args">Optional collection of arguments within the query.</param>
        /// <param name="token">A cancellation token used to cancel the asynchronous operation.</param>
        /// <returns>
        ///      A task that represents the asynchronous execution operation; the tasks result 
        ///      is an instance of <typeparamref name="TResult"/>.
        /// </returns>
        public abstract Task<TResult?> QuerySingleAsync<TResult>(string query, IDictionary<string, object?>? args = null, 
            CancellationToken token = default);

        /// <inheritdoc/>
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            GC.SuppressFinalize(this);
            await DisposeAsync().ConfigureAwait(false);
        }
    }
}

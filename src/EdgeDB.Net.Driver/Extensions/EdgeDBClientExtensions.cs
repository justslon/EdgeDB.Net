﻿using EdgeDB.Binary;
using EdgeDB.Binary.Packets;
using EdgeDB.Dumps;
using EdgeDB.Models;

namespace EdgeDB
{
    public static class EdgeDBClientExtensions
    {
        #region JsonResults
        /// <summary>
        ///     Executes a given query and returns the result as a single json string.
        /// </summary>
        /// <param name="client">The client on which to preform the query on.</param>
        /// <param name="query">The query to execute.</param>
        /// <param name="args">Optional collection of arguments within the query.</param>
        /// <returns>
        ///     A task representing the asynchronous query operation. The tasks result is 
        ///     the json result of the query.
        /// </returns>
        /// <exception cref="ResultCardinalityMismatchException">The query returned more than 1 datapoint.</exception>
        public static async Task<string> QueryJsonAsync(this EdgeDBBinaryClient client, 
            string query, IDictionary<string, object?>? args = null)
        {
            var result = await client.ExecuteInternalAsync(query, args, Cardinality.Many, format: IOFormat.Json).ConfigureAwait(false);

            if(result.Data.Count >= 2)
            {
                throw new ResultCardinalityMismatchException(Cardinality.AtMostOne, Cardinality.Many);
            }

            return result.Data.Count == 1
                ? (string)result.Deserializer.Deserialize(result.Data[0].PayloadBuffer)!
                : "[]";
        }

        /// <summary>
        ///     Executes a given query and returns the result as an array of json objects.
        /// </summary>
        /// <param name="client">The client on which to preform the query on.</param>
        /// <param name="query">The query to execute.</param>
        /// <param name="args">Optional collection of arguments within the query.</param>
        /// <returns>
        ///     A task representing the asynchronous query operation. The tasks result is 
        ///     the json result of the query.
        /// </returns>
        public static async Task<string[]> QueryJsonElementsAsync(this EdgeDBBinaryClient client,
            string query, IDictionary<string, object?>? args = null)
        {
            var result = await client.ExecuteInternalAsync(query, args, Cardinality.Many, format: IOFormat.JsonElements).ConfigureAwait(false);

            string[] elements = new string[result.Data.Count];

            for (int i = 0; i != elements.Length; i++)
                elements[i] = (string)result.Deserializer.Deserialize(result.Data[i].PayloadBuffer)!;

            return elements;
        }

        #endregion

        #region Transactions
        /// <summary>
        ///     Creates a transaction and executes a callback with the transaction object.
        /// </summary>
        /// <param name="client">The TCP client to preform the transaction with.</param>
        /// <param name="func">The callback to pass the transaction into.</param>
        /// <returns>A task that proxies the passed in callbacks awaiter.</returns>
        public static Task TransactionAsync(this ITransactibleClient client, Func<Transaction, Task> func)
            => TransactionInternalAsync(client, TransactionSettings.Default, func);

        /// <summary>
        ///     Creates a transaction and executes a callback with the transaction object.
        /// </summary>
        /// <typeparam name="TResult">The return result of the task.</typeparam>
        /// <param name="client">The TCP client to preform the transaction with.</param>
        /// <param name="func">The callback to pass the transaction into.</param>
        /// <returns>A task that proxies the passed in callbacks awaiter.</returns>
        public static async Task<TResult?> TransactionAsync<TResult>(this ITransactibleClient client, Func<Transaction, Task<TResult>> func)
        {
            TResult? result = default;

            await TransactionInternalAsync(client, TransactionSettings.Default, async (t) =>
            {
                result = await func(t).ConfigureAwait(false);
            });

            return result;
        }

        /// <summary>
        ///     Creates a transaction and executes a callback with the transaction object.
        /// </summary>
        /// <param name="client">The TCP client to preform the transaction with.</param>
        /// <param name="settings">The transactions settings.</param>
        /// <param name="func">The callback to pass the transaction into.</param>
        /// <returns>A task that proxies the passed in callbacks awaiter.</returns>
        public static Task TransactionAsync(this ITransactibleClient client, TransactionSettings settings, Func<Transaction, Task> func)
            => TransactionInternalAsync(client, settings, func);

        /// <summary>
        ///     Creates a transaction and executes a callback with the transaction object.
        /// </summary>
        /// <typeparam name="TResult">The return result of the task.</typeparam>
        /// <param name="client">The TCP client to preform the transaction with.</param>
        /// <param name="settings">The transactions settings.</param>
        /// <param name="func">The callback to pass the transaction into.</param>
        /// <returns>A task that proxies the passed in callbacks awaiter.</returns>
        public static async Task<TResult?> TransactionAsync<TResult>(this ITransactibleClient client, TransactionSettings settings, Func<Transaction, Task<TResult>> func)
        {
            TResult? result = default;

            await TransactionInternalAsync(client, settings, async (t) =>
            {
                result = await func(t).ConfigureAwait(false);
            });

            return result;
        }

        internal static async Task TransactionInternalAsync(ITransactibleClient client, TransactionSettings settings, Func<Transaction, Task> func)
        {
            var transaction = new Transaction(client, settings);

            await transaction.StartAsync().ConfigureAwait(false);

            bool commitFailed = false;

            try
            {
                await func(transaction).ConfigureAwait(false);

                try
                {
                    await transaction.CommitAsync();
                }
                catch
                {
                    commitFailed = true;
                    throw;
                }
            }
            catch (Exception)
            {
                if (!commitFailed)
                {
                    try
                    {
                        await transaction.RollbackAsync().ConfigureAwait(false);
                    }
                    catch (Exception rollbackErr) when (rollbackErr is not EdgeDBException) // see https://github.com/edgedb/edgedb-js/blob/f170b5f53eab605454704e869e083c2afc693ada/src/client.ts#L142
                    {
                        throw;
                    }
                }

                throw;
            }
        }
        #endregion

        #region Dump/restore

        /// <summary>
        ///     Dumps the current database to a stream.
        /// </summary>
        /// <param name="client">The TCP client to preform the transaction with.</param>
        /// <param name="token">A token to cancel the operation with.</param>
        /// <returns>A stream containing the entire dumped database.</returns>
        /// <exception cref="EdgeDBErrorException">The server sent an error message during the dumping process.</exception>
        /// <exception cref="EdgeDBException">The server sent a mismatched packet.</exception>
        public static async Task<Stream?> DumpDatabaseAsync(this EdgeDBBinaryClient client, CancellationToken token = default)
        {
            using var cmdLock = await client.AquireCommandLockAsync(token).ConfigureAwait(false);

            try
            {
                var tcs = new TaskCompletionSource();
                token.Register(() => tcs.SetCanceled(token));

                var stream = new MemoryStream();
                var writer = new DumpWriter(stream);

                var handler = (IReceiveable msg) =>
                {
                    switch (msg)
                    {
                        case CommandComplete:
                            tcs.TrySetResult();
                            break;
                        case DumpBlock block:
                            {
                                writer.WriteDumpBlock(block);
                            }
                            break;
                        case ErrorResponse error:
                            {
                                throw new EdgeDBErrorException(error);
                            }
                    }

                    return ValueTask.CompletedTask;
                };

                client.Duplexer.OnMessage += handler;

                var dump = await client.Duplexer.DuplexAndSyncAsync(new Dump(), x => x.Type == ServerMessageType.DumpHeader, token);

                if (dump is ErrorResponse err)
                {
                    client.Duplexer.OnMessage -= handler;
                    throw new EdgeDBErrorException(err);
                }

                if (dump is not DumpHeader dumpHeader)
                {
                    client.Duplexer.OnMessage -= handler;
                    throw new UnexpectedMessageException(ServerMessageType.DumpHeader, dump.Type);
                }

                writer.WriteDumpHeader(dumpHeader);

                await tcs.Task.ConfigureAwait(false);

                client.Duplexer.OnMessage -= handler;

                stream.Position = 0;
                return stream;
            }
            catch (Exception x) when (x is OperationCanceledException or TaskCanceledException)
            {
                throw new TimeoutException("Database dump timed out", x);
            }
        }

        /// <summary>
        ///     Restores the database based on a database dump stream.
        /// </summary>
        /// <param name="client">The TCP client to preform the transaction with.</param>
        /// <param name="stream">The stream containing the database dump.</param>
        /// <param name="token">A token to cancel the operation with.</param>
        /// <returns>The command complete packet received after restoring the database.</returns>
        /// <exception cref="EdgeDBException">
        ///     The server sent an invalid packet or the restore operation couldn't proceed 
        ///     due to the database not being empty.
        /// </exception>
        /// <exception cref="EdgeDBErrorException">The server sent an error during the restore operation.</exception>
        public static async Task<CommandComplete> RestoreDatabaseAsync(this EdgeDBBinaryClient client, Stream stream, CancellationToken token = default)
        {
            using var cmdLock = await client.AquireCommandLockAsync(token).ConfigureAwait(false);

            var reader = new DumpReader();

            var count = await client.QueryRequiredSingleAsync<long>("select count(schema::Module filter not .builtin and not .name = \"default\") + count(schema::Object filter .name like \"default::%\")", token: token).ConfigureAwait(false);

            if (count > 0)
                throw new InvalidOperationException("Cannot restore: Database isn't empty");

            var packets = DumpReader.ReadDatabaseDump(stream);

            var result = await client.Duplexer.DuplexAsync(x => x.Type == ServerMessageType.RestoreReady, token, packets.Restore).ConfigureAwait(false);

            if (result is ErrorResponse err)
                throw new EdgeDBErrorException(err);

            if (result is not RestoreReady)
                throw new UnexpectedMessageException(ServerMessageType.RestoreReady, result.Type);

            foreach (var block in packets.Blocks)
            {
                await client.Duplexer.SendAsync(block, token).ConfigureAwait(false);
            }

            result = await client.Duplexer.DuplexAsync(x => x.Type == ServerMessageType.CommandComplete, token, new RestoreEOF()).ConfigureAwait(false);

            return result is ErrorResponse error
                ? throw new EdgeDBErrorException(error)
                : result is not CommandComplete complete
                ? throw new UnexpectedMessageException(ServerMessageType.CommandComplete, result.Type)
                : complete;
        }
        #endregion
    }
}

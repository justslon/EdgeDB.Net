﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeDB.Tests.Benchmarks
{
    public class MockedEdgeDBClient : BaseEdgeDBClient
    {
        public override bool IsConnected => true;

        public MockedEdgeDBClient(ulong id)
            : base(id)
        {

        }

        public override Task ExecuteAsync(string query, IDictionary<string, object?>? args = null, CancellationToken token = default)
            => Task.CompletedTask;

        public override Task<IReadOnlyCollection<TResult?>> QueryAsync<TResult>(string query, IDictionary<string, object?>? args = null, CancellationToken token = default)
            where TResult : default
            => Task.FromResult<IReadOnlyCollection<TResult?>>(Array.Empty<TResult>());

        public override Task<TResult> QueryRequiredSingleAsync<TResult>(string query, IDictionary<string, object?>? args = null, CancellationToken token = default)
            => Task.FromResult<TResult>(default!);

        public override Task<TResult?> QuerySingleAsync<TResult>(string query, IDictionary<string, object?>? args = null, CancellationToken token = default)
            where TResult : default
            => Task.FromResult<TResult?>(default);
    }
}

﻿// The MIT License (MIT)
//
// Copyright (c) 2015 Rasmus Mikkelsen
// https://github.com/rasmus/EventFlow
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Aggregates;
using EventFlow.Core;
using EventFlow.Exceptions;
using EventFlow.Logs;
using EventFlow.MsSql;

namespace EventFlow.EventStores.MsSql
{
    public class MsSqlEventStore : EventStoreBase
    {
        public class EventDataModel : ICommittedDomainEvent
        {
            public long GlobalSequenceNumber { get; set; }
            public Guid BatchId { get; set; }
            public string AggregateId { get; set; }
            public string AggregateName { get; set; }
            public string Data { get; set; }
            public string Metadata { get; set; }
            public int AggregateSequenceNumber { get; set; }
        }

        private readonly IMsSqlConnection _connection;

        public MsSqlEventStore(
            ILog log,
            IAggregateFactory aggregateFactory,
            IEventJsonSerializer eventJsonSerializer,
            IEventUpgradeManager eventUpgradeManager,
            IEnumerable<IMetadataProvider> metadataProviders,
            IMsSqlConnection connection)
            : base(log, aggregateFactory, eventJsonSerializer, eventUpgradeManager, metadataProviders)
        {
            _connection = connection;
        }

        protected override async Task<AllCommittedEventsPage> LoadAllCommittedDomainEvents(
            GlobalPosition globalPosition,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var startPostion = globalPosition.IsStart
                ? 0
                : long.Parse(globalPosition.Value);
            var endPosition = startPostion + pageSize;

            const string sql = @"
                SELECT
                    GlobalSequenceNumber, BatchId, AggregateId, AggregateName, Data, Metadata, AggregateSequenceNumber
                FROM EventFlow
                WHERE
                    GlobalSequenceNumber >= @FromId AND GlobalSequenceNumber <= @ToId
                ORDER BY
                    GlobalSequenceNumber ASC";
            var eventDataModels = await _connection.QueryAsync<EventDataModel>(
                Label.Named("mssql-fetch-events"),
                cancellationToken,
                sql,
                new
                    {
                        FromId = startPostion,
                        ToId = endPosition,
                    })
                .ConfigureAwait(false);

            var nextPosition = eventDataModels.Any()
                ? eventDataModels.Max(e => e.GlobalSequenceNumber) + 1
                : startPostion;

            return new AllCommittedEventsPage(new GlobalPosition(nextPosition.ToString()), eventDataModels);
        }

        protected override async Task<IReadOnlyCollection<ICommittedDomainEvent>> CommitEventsAsync<TAggregate, TIdentity>(
            TIdentity id,
            IReadOnlyCollection<SerializedEvent> serializedEvents,
            CancellationToken cancellationToken)
        {
            if (!serializedEvents.Any())
            {
                return new ICommittedDomainEvent[] {};
            }

            var aggregateType = typeof(TAggregate);
            var eventDataModels = serializedEvents
                .Select((e, i) => new EventDataModel
                    {
                        AggregateId = id.Value,
                        AggregateName = e.Metadata[MetadataKeys.AggregateName],
                        BatchId = Guid.Parse(e.Metadata[MetadataKeys.BatchId]),
                        Data = e.SerializedData,
                        Metadata = e.SerializedMetadata,
                        AggregateSequenceNumber = e.AggregateSequenceNumber,
                    })
                .ToList();

            Log.Verbose(
                "Committing {0} events to MSSQL event store for aggregate {1} with ID '{2}'",
                eventDataModels.Count,
                aggregateType.Name,
                id);

            const string sql = @"
                INSERT INTO
                    EventFlow
                        (BatchId, AggregateId, AggregateName, Data, Metadata, AggregateSequenceNumber)
                        OUTPUT CAST(INSERTED.GlobalSequenceNumber as bigint)
                    SELECT
                        BatchId, AggregateId, AggregateName, Data, Metadata, AggregateSequenceNumber
                    FROM
                        @rows
                    ORDER BY AggregateSequenceNumber ASC";

            IReadOnlyCollection<long> ids;
            try
            {
                ids = await _connection.InsertMultipleAsync<long, EventDataModel>(
                    Label.Named("mssql-insert-events"), 
                    cancellationToken,
                    sql,
                    eventDataModels)
                    .ConfigureAwait(false);
            }
            catch (SqlException exception)
            {
                if (exception.Number == 2601)
                {
                    Log.Verbose(
                        "MSSQL event insert detected an optimistic concurrency exception for aggregate '{0}' with ID '{1}'",
                        aggregateType.Name,
                        id);
                    throw new OptimisticConcurrencyException(exception.Message, exception);
                }

                throw;
            }

            eventDataModels = eventDataModels
                .Zip(
                    ids,
                    (e, i) =>
                        {
                            e.GlobalSequenceNumber = i;
                            return e;
                        })
                .ToList();

            return eventDataModels;
        }

        protected override async Task<IReadOnlyCollection<ICommittedDomainEvent>> LoadCommittedEventsAsync<TAggregate, TIdentity>(
            TIdentity id,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT
                    GlobalSequenceNumber, BatchId, AggregateId, AggregateName, Data, Metadata, AggregateSequenceNumber
                FROM EventFlow
                WHERE
                    AggregateId = @AggregateId
                ORDER BY
                    AggregateSequenceNumber ASC";
            var eventDataModels = await _connection.QueryAsync<EventDataModel>(
                Label.Named("mssql-fetch-events"), 
                cancellationToken,
                sql,
                new
                    {
                        AggregateId = id.Value
                    })
                .ConfigureAwait(false);
            return eventDataModels;
        }

        public override async Task DeleteAggregateAsync<TAggregate, TIdentity>(
            TIdentity id,
            CancellationToken cancellationToken)
        {
            const string sql = @"DELETE FROM EventFlow WHERE AggregateId = @AggregateId";
            var affectedRows = await _connection.ExecuteAsync(
                Label.Named("mssql-delete-aggregate"),
                cancellationToken,
                sql,
                new {AggregateId = id.Value})
                .ConfigureAwait(false);

            Log.Verbose(
                "Deleted aggregate '{0}' with ID '{1}' by deleting all of its {2} events",
                typeof(TAggregate).Name,
                id,
                affectedRows);
        }
    }
}

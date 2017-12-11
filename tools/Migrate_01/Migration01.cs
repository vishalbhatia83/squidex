﻿// ==========================================================================
//  MigrateToEntities.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using System.Threading.Tasks;
using System.Timers;
using Squidex.Domain.Apps.Core.Schemas;
using Squidex.Domain.Apps.Entities.Apps;
using Squidex.Domain.Apps.Entities.Assets;
using Squidex.Domain.Apps.Entities.Contents;
using Squidex.Domain.Apps.Entities.Schemas;
using Squidex.Domain.Apps.Events;
using Squidex.Domain.Apps.Events.Assets;
using Squidex.Domain.Apps.Events.Contents;
using Squidex.Infrastructure;
using Squidex.Infrastructure.EventSourcing;
using Squidex.Infrastructure.Migrations;
using Squidex.Infrastructure.States;
using Squidex.Infrastructure.Tasks;

namespace Migrate_01
{
    public sealed class Migration01 : IMigration, IEventSubscriber
    {
        private readonly FieldRegistry fieldRegistry;
        private readonly IEventStore eventStore;
        private readonly IEventDataFormatter eventDataFormatter;
        private readonly IStateFactory stateFactory;
        private readonly Timer timer = new Timer { AutoReset = false, Interval = 5000 };
        private readonly TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();

        public int FromVersion { get; } = 0;

        public int ToVersion { get; } = 1;

        public Migration01(
            FieldRegistry fieldRegistry,
            IEventDataFormatter eventDataFormatter,
            IEventStore eventStore,
            IStateFactory stateFactory)
        {
            this.fieldRegistry = fieldRegistry;
            this.eventDataFormatter = eventDataFormatter;
            this.eventStore = eventStore;
            this.stateFactory = stateFactory;

            timer.Elapsed += (sender, e) =>
            {
                tcs.TrySetResult(true);
            };
        }

        public async Task UpdateAsync()
        {
            var subscription = eventStore.CreateSubscription(this, ".*");

            await tcs.Task;
            await subscription.StopAsync();
        }

        public async Task OnEventAsync(IEventSubscription subscription, StoredEvent storedEvent)
        {
            var @event = ParseKnownEvent(storedEvent);

            if (@event != null)
            {
                var version = storedEvent.EventStreamNumber;

                if (@event.Payload is AssetEvent assetEvent)
                {
                    var asset = await stateFactory.CreateAsync<AssetDomainObject>(assetEvent.AssetId);

                    asset.UpdateState(asset.State.Apply(@event));

                    await asset.WriteStateAsync(version);
                }
                else if (@event.Payload is ContentEvent contentEvent)
                {
                    var content = await stateFactory.CreateAsync<ContentDomainObject>(contentEvent.ContentId);

                    content.UpdateState(content.State.Apply(@event));

                    await content.WriteStateAsync(version);
                }
                else if (@event.Payload is SchemaEvent schemaEvent)
                {
                    var schema = await stateFactory.GetSingleAsync<SchemaDomainObject>(schemaEvent.SchemaId.Id);

                    schema.UpdateState(schema.State.Apply(@event, fieldRegistry));

                    await schema.WriteStateAsync(version);
                }
                else if (@event.Payload is AppEvent appEvent)
                {
                    var app = await stateFactory.GetSingleAsync<AppDomainObject>(appEvent.AppId.Id);

                    app.UpdateState(app.State.Apply(@event));

                    await app.WriteStateAsync(version);
                }
            }

            timer.Stop();
            timer.Start();
        }

        public Task OnErrorAsync(IEventSubscription subscription, Exception exception)
        {
            return TaskHelper.Done;
        }

        private Envelope<IEvent> ParseKnownEvent(StoredEvent storedEvent)
        {
            try
            {
                return eventDataFormatter.Parse(storedEvent.Data);
            }
            catch (TypeNameNotFoundException)
            {
                return null;
            }
        }
    }
}

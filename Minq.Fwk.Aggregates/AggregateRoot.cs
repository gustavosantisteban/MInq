﻿using Minq.Fwk.Core;
using System;
using System.Collections.Generic;

namespace Minq.Fwk.Aggregates
{
    public abstract class AggregateRoot<TAggregate, TIdentity> : IAggregateRoot<TIdentity>
        where TAggregate : AggregateRoot<TAggregate, TIdentity>
        where TIdentity : IIdentity
    {
        private static readonly IReadOnlyDictionary<Type, Action<TAggregate, IAggregateEvent>> ApplyMethods;
        private static readonly IAggregateName AggregateName = typeof(TAggregate).GetAggregateName();
        private readonly List<IUncommittedEvent> _uncommittedEvents = new List<IUncommittedEvent>();
        private CircularBuffer<ISourceId> _previousSourceIds = new CircularBuffer<ISourceId>(10);

        public IAggregateName Name => AggregateName;
        public TIdentity Id { get; }
        public int Version { get; protected set; }
        public bool IsNew => Version <= 0;
        public IEnumerable<IUncommittedEvent> UncommittedEvents => _uncommittedEvents;

        static AggregateRoot()
        {
            ApplyMethods = typeof(TAggregate).GetAggregateEventApplyMethods<TAggregate, TIdentity, TAggregate>();
        }

        protected AggregateRoot(TIdentity id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if ((this as TAggregate) == null)
            {
                throw new InvalidOperationException(
                    $"Aggregate '{GetType().PrettyPrint()}' specifies '{typeof(TAggregate).PrettyPrint()}' as generic argument, it should be its own type");
            }

            Id = id;
        }

        protected void SetSourceIdHistory(int count)
        {
            _previousSourceIds = new CircularBuffer<ISourceId>(count);
        }

        public bool HasSourceId(ISourceId sourceId)
        {
            return !sourceId.IsNone() && _previousSourceIds.Any(s => s.Value == sourceId.Value);
        }

        protected virtual void Emit<TEvent>(TEvent aggregateEvent, IMetadata metadata = null)
            where TEvent : IAggregateEvent<TAggregate, TIdentity>
        {
            if (aggregateEvent == null) throw new ArgumentNullException(nameof(aggregateEvent));

            var aggregateSequenceNumber = Version + 1;
            var eventId = EventId.NewDeterministic(
                GuidFactories.Deterministic.Namespaces.Events,
                $"{Id.Value}-v{aggregateSequenceNumber}");
            var now = DateTimeOffset.Now;
            var eventMetadata = new Metadata
            {
                Timestamp = now,
                AggregateSequenceNumber = aggregateSequenceNumber,
                AggregateName = Name.Value,
                AggregateId = Id.Value,
                EventId = eventId
            };
            eventMetadata.Add(MetadataKeys.TimestampEpoch, now.ToUnixTime().ToString());
            if (metadata != null)
            {
                eventMetadata.AddRange(metadata);
            }

            var uncommittedEvent = new UncommittedEvent(aggregateEvent, eventMetadata);

            ApplyEvent(aggregateEvent);
            _uncommittedEvents.Add(uncommittedEvent);
        }

        public virtual async Task LoadAsync(
            IEventStore eventStore,
            ISnapshotStore snapshotStore,
            CancellationToken cancellationToken)
        {
            if (eventStore == null) throw new ArgumentNullException(nameof(eventStore));

            var domainEvents = await eventStore.LoadEventsAsync<TAggregate, TIdentity>(Id, cancellationToken).ConfigureAwait(false);

            ApplyEvents(domainEvents);
        }

        public virtual async Task<IReadOnlyCollection<IDomainEvent>> CommitAsync(
            IEventStore eventStore,
            ISnapshotStore snapshotStore,
            ISourceId sourceId,
            CancellationToken cancellationToken)
        {
            if (eventStore == null) throw new ArgumentNullException(nameof(eventStore));

            var domainEvents = await eventStore.StoreAsync<TAggregate, TIdentity>(
                Id,
                _uncommittedEvents,
                sourceId,
                cancellationToken)
                .ConfigureAwait(false);
            _uncommittedEvents.Clear();
            return domainEvents;
        }

        public void ApplyEvents(IReadOnlyCollection<IDomainEvent> domainEvents)
        {
            if (domainEvents == null) throw new ArgumentNullException(nameof(domainEvents));

            foreach (var domainEvent in domainEvents)
            {
                if (domainEvent.AggregateSequenceNumber != Version + 1)
                    throw new InvalidOperationException(
                        $"Cannot apply aggregate event of type '{domainEvent.GetType().PrettyPrint()}' " +
                        $"with SequenceNumber {domainEvent.AggregateSequenceNumber} on aggregate " +
                        $"with version {Version}");

                var aggregateEvent = domainEvent.GetAggregateEvent();
                if (!(aggregateEvent is IAggregateEvent<TAggregate, TIdentity> e))
                {
                    throw new ArgumentException($"Aggregate event of type '{domainEvent.GetType()}' does not belong with aggregate '{this}'");
                }

                ApplyEvent(e);
            }

            foreach (var domainEvent in domainEvents.Where(e => e.Metadata.ContainsKey(MetadataKeys.SourceId)))
            {
                _previousSourceIds.Put(domainEvent.Metadata.SourceId);
            }
        }

        public IIdentity GetIdentity()
        {
            return Id;
        }

        protected virtual void ApplyEvent(IAggregateEvent<TAggregate, TIdentity> aggregateEvent)
        {
            if (aggregateEvent == null) throw new ArgumentNullException(nameof(aggregateEvent));

            var eventType = aggregateEvent.GetType();
            if (_eventHandlers.ContainsKey(eventType))
            {
                _eventHandlers[eventType](aggregateEvent);
            }
            else if (_eventAppliers.Any(ea => ea.Apply((TAggregate)this, aggregateEvent)))
            {
                // Already done
            }
            else
            {
                if (!ApplyMethods.TryGetValue(eventType, out var applyMethod))
                {
                    throw new NotImplementedException(
                        $"Aggregate '{Name}' does have an 'Apply' method that takes aggregate event '{eventType.PrettyPrint()}' as argument");
                }

                applyMethod(this as TAggregate, aggregateEvent);
            }

            Version++;
        }

        private readonly Dictionary<Type, Action<object>> _eventHandlers = new Dictionary<Type, Action<object>>();
        protected void Register<TAggregateEvent>(Action<TAggregateEvent> handler)
            where TAggregateEvent : IAggregateEvent<TAggregate, TIdentity>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(TAggregateEvent);
            if (_eventHandlers.ContainsKey(eventType))
            {
                throw new ArgumentException($"There's already a event handler registered for the aggregate event '{eventType.PrettyPrint()}'");
            }
            _eventHandlers[eventType] = e => handler((TAggregateEvent)e);
        }

        private readonly List<IEventApplier<TAggregate, TIdentity>> _eventAppliers = new List<IEventApplier<TAggregate, TIdentity>>();

        protected void Register(IEventApplier<TAggregate, TIdentity> eventApplier)
        {
            if (eventApplier == null) throw new ArgumentNullException(nameof(eventApplier));

            _eventAppliers.Add(eventApplier);
        }

        public override string ToString()
        {
            return $"{GetType().PrettyPrint()} v{Version}(-{_uncommittedEvents.Count})";
        }
    }
}

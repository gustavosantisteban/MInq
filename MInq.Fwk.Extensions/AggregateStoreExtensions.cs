using Minq.Fwk.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MInq.Fwk.Extensions
{
    public static class AggregateStoreExtensions
    {
        public static TAggregate Load<TAggregate, TIdentity>(
           this IAggregateStore aggregateStore,
           TIdentity id)
           where TAggregate : IAggregateRoot<TIdentity>
           where TIdentity : IIdentity
        {
            return aggregateStore.Load<TAggregate, TIdentity>(id, CancellationToken.None);
        }

        public static TAggregate Load<TAggregate, TIdentity>(
            this IAggregateStore aggregateStore,
            TIdentity id,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            var aggregate = default(TAggregate);

            using (var a = AsyncHelper.Wait)
            {
                a.Run(aggregateStore.LoadAsync<TAggregate, TIdentity>(id, cancellationToken), r => aggregate = r);
            }

            return aggregate;
        }

        public static IReadOnlyCollection<IDomainEvent> Update<TAggregate, TIdentity>(
            this IAggregateStore aggregateStore,
            TIdentity id,
            ISourceId sourceId,
            Action<TAggregate> updateAggregate)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            return aggregateStore.Update<TAggregate, TIdentity>(
                id,
                sourceId,
                (a, c) =>
                {
                    updateAggregate(a);
                    return Task.FromResult(0);
                },
                CancellationToken.None);
        }

        public static IReadOnlyCollection<IDomainEvent> Update<TAggregate, TIdentity>(
            this IAggregateStore aggregateStore,
            TIdentity id,
            ISourceId sourceId,
            Func<TAggregate, CancellationToken, Task> updateAggregate,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            IReadOnlyCollection<IDomainEvent> domainEvents = null;

            using (var a = AsyncHelper.Wait)
            {
                a.Run(aggregateStore.UpdateAsync(id, sourceId, updateAggregate, cancellationToken), r => domainEvents = r);
            }

            return domainEvents;
        }

        public static IReadOnlyCollection<IDomainEvent> Store<TAggregate, TIdentity>(
            this IAggregateStore aggregateStore,
            TAggregate aggregate,
            ISourceId sourceId)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            return aggregateStore.Store<TAggregate, TIdentity>(aggregate, sourceId, CancellationToken.None);
        }

        public static IReadOnlyCollection<IDomainEvent> Store<TAggregate, TIdentity>(
            this IAggregateStore aggregateStore,
            TAggregate aggregate,
            ISourceId sourceId,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
        {
            IReadOnlyCollection<IDomainEvent> domainEvents = null;

            using (var a = AsyncHelper.Wait)
            {
                a.Run(aggregateStore.StoreAsync<TAggregate, TIdentity>(aggregate, sourceId, cancellationToken), r => domainEvents = r);
            }

            return domainEvents;
        }
    }
}

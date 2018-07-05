using Minq.Fwk.Core;

namespace Minq.Fwk.Aggregates
{
    public interface IAggregateRoot
    {
        IIdentity GetIdentity();
    }

    public interface IAggregateRoot<out TIdentity> : IAggregateRoot
        where TIdentity : IIdentity
    {
        TIdentity Id { get; }
    }
}

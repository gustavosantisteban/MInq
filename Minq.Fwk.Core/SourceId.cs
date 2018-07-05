using MInq.Fwk.ValueObjects;
using System;

namespace Minq.Fwk.Core
{
    public class SourceId : SingleValueObject<string>, ISourceId
    {
        public static ISourceId New => new SourceId(Guid.NewGuid().ToString("D"));

        public SourceId(string value) : base(value)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException(nameof(value));
        }
    }
}

namespace Flowgent.Persistence;

using System.Text.Json.Nodes;
using Flowgent.Contracts.Api;
using Flowgent.Contracts.Runs;
using Flowgent.Engine.Core.Runs;
using Microsoft.EntityFrameworkCore;

/// <summary>Monotonic sequence numbers for ordering events and inbox messages.</summary>
internal sealed class SequenceGenerator
{
    private long _value;

    public long Next() => Interlocked.Increment(ref _value);
}

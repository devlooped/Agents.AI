using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;

namespace Devlooped.Agents.AI;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
class DynamicContextProvider(AIContextProvider provider, string key) : AIContextProvider
{
    public AIContextProvider Provider => provider;

    public override IReadOnlyList<string> StateKeys => [$"{nameof(AIContextProvider)}-{key}"];

    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
        => provider.InvokedAsync(context, cancellationToken);

    protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
        => provider.InvokingAsync(context, cancellationToken);

    string DebuggerDisplay => $"Keys = [{string.Join(", ", StateKeys)}]";
}

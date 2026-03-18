using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Agents.AI;

namespace Devlooped.Agents.AI;

/// <summary>A context provider that always injects a static <see cref="AIContext"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class StaticContextProvider(AIContext context, string key, bool synthesized = false) : AIContextProvider
{
    /// <summary>The static context to provide on agent invocations.</summary>
    public AIContext Context => context;

    /// <summary>Whether the context has been synthesized rather than configured or imported.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool Synthesized => synthesized;

    /// <inheritdoc/>
    public override IReadOnlyList<string> StateKeys => [$"{nameof(AIContext)}-{key}"];

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Context);

    string DebuggerDisplay => $"Keys = [{string.Join(", ", StateKeys)}]";
}

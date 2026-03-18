using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Devlooped.Agents.AI;

/// <summary>
/// Miscenalleous extension methods for agents.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AgentExtensions
{
    static readonly Func<AgentResponse, ChatResponse> asChatResponse = CreateAsChatResponse();

    /// <summary>Ensures the returned <see cref="ChatResponse"/> contains the <see cref="AgentResponse.AgentId"/> as an additional property.</summary>
    /// <devdoc>Change priority to -10 and make EditorBrowsable.Never when https://github.com/microsoft/agent-framework/issues/1574 is fixed.</devdoc>
    [OverloadResolutionPriority(10)]
    public static ChatResponse AsChatResponse(this AgentResponse response)
    {
        var chatResponse = asChatResponse(response);

        chatResponse.AdditionalProperties ??= [];
        chatResponse.AdditionalProperties[nameof(response.AgentId)] = response.AgentId;

        return chatResponse;
    }

    static Func<AgentResponse, ChatResponse> CreateAsChatResponse()
    {
        var method = typeof(AgentResponse).Assembly
            .GetType("Microsoft.Agents.AI.AgentResponseExtensions")
            ?.GetMethod("AsChatResponse", [typeof(AgentResponse)]);

        if (method is null)
            throw new InvalidOperationException("Could not resolve Microsoft.Agents.AI.AgentResponseExtensions.AsChatResponse.");

        return response => (ChatResponse)method.Invoke(null, [response])!;
    }

    extension(AIAgent agent)
    {
        /// <summary>Gets the emoji associated with the agent, if any.</summary>
        public string? Emoji => agent is not IHasAdditionalProperties additional
            ? null
            : additional.AdditionalProperties is null
            ? null
            : additional.AdditionalProperties.TryGetValue("Emoji", out var value) ? value as string : null;
    }
}

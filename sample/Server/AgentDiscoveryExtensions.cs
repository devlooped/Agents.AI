using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;

static class AgentDiscoveryExtensions
{
    public static void MapAgentDiscovery(this IEndpointRouteBuilder endpoints, IServiceCollection services, [StringSyntax("Route")] string path)
    {
        var agentNames = services.AsEnumerable()
            .Where(x => x.ServiceType == typeof(AIAgent) && x.IsKeyedService && x.ServiceKey is string)
            .Select(x => (string)x.ServiceKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet("/", (IServiceProvider serviceProvider)
            => Results.Ok(agentNames
                .Select(name => serviceProvider.GetRequiredKeyedService<AIAgent>(name))
                .Select(agent => new AgentDiscoveryCard(agent.Name!, agent.Description))
                .ToArray()))
            .WithName("GetAgents");
    }

    record AgentDiscoveryCard(string Name, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);
}

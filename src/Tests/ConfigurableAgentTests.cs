using System.Linq;
using Devlooped.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Devlooped.Agents.AI;

public class ConfigurableAgentTests
{
    [Fact]
    public void CanConfigureAgent()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:chat:modelid"] = "gpt-4.1-nano",
            ["ai:clients:chat:apikey"] = "sk-asdfasdf",
            ["ai:agents:bot:client"] = "chat",
            ["ai:agents:bot:name"] = "chat",
            ["ai:agents:bot:description"] = "Helpful chat agent",
            ["ai:agents:bot:instructions"] = "You are a helpful chat agent.",
            ["ai:agents:bot:emoji"] = "🤖",
        });

        builder.AddAIAgents();
        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("chat");

        Assert.Equal("chat", agent.Name);
        Assert.Equal("Helpful chat agent", agent.Description);
        Assert.Equal("You are a helpful chat agent.", agent.GetService<ChatClientAgentOptions>()?.ChatOptions?.Instructions);

        var additional = Assert.IsAssignableFrom<IHasAdditionalProperties>(agent);
        Assert.Equal("🤖", additional.AdditionalProperties?["emoji"]?.ToString());
        Assert.Equal("🤖", agent.Emoji);
    }

    [Fact]
    public void CanGetFromAlternativeKey()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:Chat:modelid"] = "gpt-4.1-nano",
            ["ai:clients:Chat:apikey"] = "sk-asdfasdf",
            ["ai:agents:bot:client"] = "chat",
        });

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>(new ServiceKey("Bot"));

        Assert.Equal("bot", agent.Name);
        Assert.Same(agent, app.Services.GetAIAgent("Bot"));
    }

    [Fact]
    public void CanGetSectionAndIdFromMetadata()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:chat:modelid"] = "gpt-4.1-nano",
            ["ai:clients:chat:apikey"] = "sk-asdfasdf",
            ["ai:agents:bot:client"] = "chat",
        });

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("bot");
        var metadata = agent.GetService<ConfigurableAgentMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("bot", metadata.Name);
        Assert.Equal("ai:agents:bot", metadata.Section);
    }

    [Fact]
    public void DedentsDescriptionAndInstructions()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:chat:modelid"] = "gpt-4.1-nano",
            ["ai:clients:chat:apikey"] = "sk-asdfasdf",
            ["ai:agents:bot:client"] = "chat",
            ["ai:agents:bot:name"] = "chat",
            ["ai:agents:bot:description"] = """


                    Line 1
                    Line 2
                    Line 3

                """,
            ["ai:agents:bot:instructions"] = """
                        Agent Instructions:
                            - Step 1
                            - Step 2
                            - Step 3
                """,
        });

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("chat");

        Assert.Equal("""
            Line 1
            Line 2
            Line 3
            """, agent.Description);

        Assert.Equal("""
            Agent Instructions:
                - Step 1
                - Step 2
                - Step 3
            """, agent.GetService<ChatClientAgentOptions>()?.ChatOptions?.Instructions);
    }

    [Fact]
    public void CanReloadConfiguration()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:openai:modelid"] = "gpt-4.1-nano",
            ["ai:clients:openai:apikey"] = "sk-asdfasdf",
            ["ai:clients:grok:modelid"] = "grok-4",
            ["ai:clients:grok:apikey"] = "xai-asdfasdf",
            ["ai:clients:grok:endpoint"] = "https://api.x.ai",
            ["ai:agents:bot:client"] = "openai",
            ["ai:agents:bot:description"] = "Helpful chat agent",
            ["ai:agents:bot:instructions"] = "You are a helpful agent.",
        });

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("bot");

        Assert.Equal("Helpful chat agent", agent.Description);
        Assert.Equal("You are a helpful agent.", agent.GetService<ChatClientAgentOptions>()?.ChatOptions?.Instructions);
        Assert.Equal("openai", agent.GetService<AIAgentMetadata>()?.ProviderName);

        var configuration = (IConfigurationRoot)app.Services.GetRequiredService<IConfiguration>();
        configuration["ai:agents:bot:client"] = "grok";
        configuration["ai:agents:bot:description"] = "Very helpful chat agent";
        configuration["ai:agents:bot:instructions"] = "You are a very helpful chat agent.";
        configuration.Reload();

        Assert.Equal("Very helpful chat agent", agent.Description);
        Assert.Equal("You are a very helpful chat agent.", agent.GetService<ChatClientAgentOptions>()?.ChatOptions?.Instructions);
        Assert.Equal("xai", agent.GetService<AIAgentMetadata>()?.ProviderName);
    }

    [Fact]
    public void AssignsChatHistoryProviderFromKeyedService()
    {
        var builder = new HostApplicationBuilder();
        var history = new TestChatHistoryProvider();

        builder.Services.AddKeyedSingleton<ChatHistoryProvider>("bot", history);

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:chat:modelid"] = "gpt-4.1-nano",
            ["ai:clients:chat:apikey"] = "sk-asdfasdf",
            ["ai:agents:bot:client"] = "chat",
        });

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("bot");
        var options = agent.GetService<ChatClientAgentOptions>();

        Assert.Same(history, options?.ChatHistoryProvider);
    }

    [Fact]
    public void AssignsChatHistoryProviderFromService()
    {
        var builder = new HostApplicationBuilder();
        var history = new TestChatHistoryProvider();

        builder.Services.AddSingleton<ChatHistoryProvider>(history);

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai:clients:chat:modelid"] = "gpt-4.1-nano",
            ["ai:clients:chat:apikey"] = "sk-asdfasdf",
            ["ai:agents:bot:client"] = "chat",
        });

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("bot");
        var options = agent.GetService<ChatClientAgentOptions>();

        Assert.Same(history, options?.ChatHistoryProvider);
    }

    [Fact]
    public async Task UseAndAIContextProvidersAreCombined()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddToml(
            """
            [ai.clients.openai]
            modelid = "gpt-4.1"
            apikey = "sk-asdf"

            [ai.agents.chat]
            description = "Chat agent."
            client = "openai"
            use = ["voseo"]

            [ai.context.voseo]
            instructions = "Hablas en voseo."
            """);

        builder.AddAIAgents(configureOptions: (_, options) =>
        {
            options.AIContextProviders =
            [
                .. options.AIContextProviders ?? [],
                new TestAIContextProvider(new AIContext
                {
                    Instructions = "You prefer concise answers."
                })
            ];
        });

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("chat");
        var options = agent.GetService<ChatClientAgentOptions>();
        var context = await AggregateContextAsync((options?.AIContextProviders ?? []).ToList());

        Assert.Contains("Hablas en voseo.", context.Instructions);
        Assert.Contains("You prefer concise answers.", context.Instructions);
    }

    [Fact]
    public async Task UseAndExportedAIContextAreCombined()
    {
        var builder = new HostApplicationBuilder();

        builder.Services.AddKeyedSingleton<AIContextProvider>("chat", new TestAIContextProvider(new AIContext
        {
            Instructions = "You prefer concise answers."
        }));

        builder.Configuration.AddToml(
            """
            [ai.clients.openai]
            modelid = "gpt-4.1"
            apikey = "sk-asdf"

            [ai.agents.chat]
            description = "Chat agent."
            client = "openai"
            use = ["voseo"]

            [ai.context.voseo]
            instructions = "Hablas en voseo."
            """);

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("chat");
        var options = agent.GetService<ChatClientAgentOptions>();
        var context = await AggregateContextAsync((options?.AIContextProviders ?? []).ToList());

        Assert.Contains("Hablas en voseo.", context.Instructions);
        Assert.Contains("You prefer concise answers.", context.Instructions);
    }

    [Fact]
    public async Task UsesConfiguredContextsAndTools()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddToml(
            """
            [ai.clients.openai]
            modelid = "gpt-4.1"
            apikey = "sk-asdf"

            [ai.agents.chat]
            description = "Chat agent."
            client = "openai"
            use = ["default", "static", "dynamic"]
            tools = ["get_foo"]

            [ai.context.default]
            instructions = 'foo'
            messages = [
                { system = "You are strictly professional." },
                { user = "Hey you!"},
                { assistant = "Hello there. How can I assist you today?" }
            ]
            tools = ["get_date"]
            """);

        var getDate = AIFunctionFactory.Create(() => DateTimeOffset.Now, "get_date");
        var getFoo = AIFunctionFactory.Create(() => "foo", "get_foo");
        var getBaz = AIFunctionFactory.Create(() => "baz", "get_baz");

        builder.Services.AddKeyedSingleton("get_date", (AITool)getDate);
        builder.Services.AddKeyedSingleton("get_foo", (AITool)getFoo);
        builder.Services.AddKeyedSingleton("static", new AIContext { Instructions = "bar" });
        builder.Services.AddKeyedSingleton<AIContextProvider>("dynamic", new TestAIContextProvider(new AIContext
        {
            Instructions = "baz",
            Tools = new[] { getBaz }
        }));

        builder.AddAIAgents();

        var app = builder.Build();
        var agent = app.Services.GetRequiredKeyedService<AIAgent>("chat");
        var options = agent.GetService<ChatClientAgentOptions>();

        Assert.NotNull(options?.AIContextProviders);
        var merged = await AggregateContextAsync((options.AIContextProviders ?? []).ToList());

        Assert.Contains("foo", merged.Instructions);
        Assert.Contains("bar", merged.Instructions);
        Assert.Contains("baz", merged.Instructions);

        Assert.Equal(3, merged.Messages?.Count());
        Assert.Contains(getDate, merged.Tools!);
        Assert.Contains(getFoo, merged.Tools!);
        Assert.Contains(getBaz, merged.Tools!);
    }

    [Fact]
    public void OverrideModelFromAgentModel()
    {
        var builder = new HostApplicationBuilder();

        builder.Configuration.AddToml(
            """
            [ai.clients.openai]
            modelid = "gpt-4.1"
            apikey = "sk-asdf"

            [ai.agents.chat]
            description = "Chat"
            client = "openai"
            model = "gpt-5"
            """);

        builder.AddAIAgents();
        var app = builder.Build();

        var agent = app.Services.GetRequiredKeyedService<AIAgent>("chat");
        var options = agent.GetService<ChatClientAgentOptions>();

        Assert.Equal("gpt-5", options?.ChatOptions?.ModelId);
    }

    static async Task<AIContext> AggregateContextAsync(IReadOnlyList<AIContextProvider> providers)
    {
        var merged = new AIContext();
        var instructions = new List<string>();
        var messages = new List<ChatMessage>();
        var tools = new List<AITool>();

        foreach (var provider in providers)
        {
            var invoking = new AIContextProvider.InvokingContext(Mock.Of<AIAgent>(), Mock.Of<AgentSession>(), merged);
            var context = await provider.InvokingAsync(invoking, default);

            if (!string.IsNullOrWhiteSpace(context.Instructions))
                instructions.Add(context.Instructions);

            if (context.Messages?.Any() == true)
                messages.AddRange(context.Messages);

            if (context.Tools?.Any() == true)
                tools.AddRange(context.Tools);
        }

        if (instructions.Count > 0)
            merged.Instructions = string.Join('\n', instructions);

        if (messages.Count > 0)
            merged.Messages = messages;

        if (tools.Count > 0)
            merged.Tools = tools;

        return merged;
    }

    sealed class TestAIContextProvider(AIContext context) : AIContextProvider
    {
        readonly AIContext providedContext = context;

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(providedContext);
    }

    sealed class TestChatHistoryProvider(IEnumerable<ChatMessage>? messages = null) : ChatHistoryProvider
    {
        readonly IEnumerable<ChatMessage> messages = messages ?? [];

        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(messages);
    }
}


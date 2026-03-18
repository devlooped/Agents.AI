using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Devlooped.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Devlooped.Agents.AI;

/// <summary>
/// A configuration-driven <see cref="AIAgent"/> which monitors configuration changes and
/// re-applies them to the inner agent automatically.
/// </summary>
public sealed partial class ConfigurableAgent : AIAgent, IHasAdditionalProperties, IDisposable
{
    readonly IServiceProvider services;
    readonly IConfiguration configuration;
    readonly string section;
    readonly string name;
    readonly ILogger logger;
    readonly Action<string, ChatClientAgentOptions>? configure;

    IDisposable reloadToken;
    RuntimeState runtime;

    public ConfigurableAgent(IServiceProvider services, string section, string name, Action<string, ChatClientAgentOptions>? configure)
    {
        if (section.Contains('.'))
            throw new ArgumentException("Section separator must be ':', not '.'");

        this.services = Throw.IfNull(services);
        this.configuration = services.GetRequiredService<IConfiguration>();
        this.logger = services.GetRequiredService<ILogger<ConfigurableAgent>>();
        this.section = Throw.IfNullOrEmpty(section);
        this.name = Throw.IfNullOrEmpty(name);
        this.configure = configure;

        runtime = Configure(configuration.GetRequiredSection(section));
        reloadToken = configuration.GetReloadToken().RegisterChangeCallback(OnReload, state: null);
    }

    /// <summary>Disposes the agent and stops monitoring configuration changes.</summary>
    public void Dispose()
    {
        reloadToken.Dispose();
        (runtime.ChatClient as IDisposable)?.Dispose();
    }

    /// <inheritdoc/>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <inheritdoc/>
    protected override string? IdCore => runtime.Agent.Id;

    /// <inheritdoc/>
    public override string? Name => name;

    /// <inheritdoc/>
    public override string? Description => runtime.Agent.Description;

    /// <summary>Configured chat client agent options.</summary>
    public ChatClientAgentOptions Options => runtime.AgentOptions;

    /// <summary>The configuration that created the <see cref="Options"/>, for troubleshooting.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [JsonIgnore]
    public JsonElement Configuration => runtime.ConfiguredJson;

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null) => serviceType switch
    {
        Type t when t == typeof(ChatClientAgentOptions) => runtime.AgentOptions,
        Type t when t == typeof(IChatClient) => runtime.ChatClient,
        Type t when t == typeof(ConfigurableAgentMetadata) => runtime.ConfigurableMetadata,
        Type t when t == typeof(AIAgentMetadata) => runtime.AgentMetadata,
        _ => runtime.Agent.GetService(serviceType, serviceKey)
    };

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => runtime.Agent.CreateSessionAsync(cancellationToken);

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => runtime.Agent.SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken);

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedSession, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => runtime.Agent.DeserializeSessionAsync(serializedSession, jsonSerializerOptions, cancellationToken);

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => runtime.Agent.RunAsync(messages, session, options, cancellationToken);

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => runtime.Agent.RunStreamingAsync(messages, session, options, cancellationToken);

    RuntimeState Configure(IConfigurationSection configSection)
    {
        var configuredOptions = configSection.Get<ConfigurableAgentOptions>() ?? new ConfigurableAgentOptions();
        configuredOptions.Name ??= name;
        configuredOptions.Description = configuredOptions.Description?.Dedent();
        configuredOptions.Instructions = configuredOptions.Instructions?.Dedent();

        var properties = configSection.Get<AdditionalPropertiesDictionary>();
        if (properties is not null)
        {
            properties.Remove(nameof(ConfigurableAgentOptions.Name));
            properties.Remove(nameof(ConfigurableAgentOptions.Description));
            properties.Remove(nameof(ConfigurableAgentOptions.Instructions));
            properties.Remove(nameof(ConfigurableAgentOptions.Client));
            properties.Remove(nameof(ConfigurableAgentOptions.Model));
            properties.Remove(nameof(ConfigurableAgentOptions.Use));
            properties.Remove(nameof(ConfigurableAgentOptions.Skills));
            properties.Remove(nameof(ConfigurableAgentOptions.Tools));
            properties.Remove(nameof(ConfigurableAgentOptions.UseProvidedChatClientAsIs));
            properties.Remove(nameof(ConfigurableAgentOptions.ClearOnChatHistoryProviderConflict));
            properties.Remove(nameof(ConfigurableAgentOptions.WarnOnChatHistoryProviderConflict));
            properties.Remove(nameof(ConfigurableAgentOptions.ThrowOnChatHistoryProviderConflict));
            AdditionalProperties = properties;
        }

        if (configuration[$"{section}:name"] is { } newName && !string.Equals(newName, name, StringComparison.Ordinal))
            throw new InvalidOperationException($"The name of a configured agent cannot be changed at runtime. Expected '{name}' but was '{newName}'.");

        var chatClientKey = configuredOptions.Client
            ?? throw new InvalidOperationException($"A client must be specified for agent '{name}' in configuration section '{section}'.");

        var client = services.GetKeyedService<IChatClient>(chatClientKey)
            ?? services.GetKeyedService<IChatClient>(new ServiceKey(chatClientKey))
            ?? throw new InvalidOperationException($"Specified chat client '{chatClientKey}' for agent '{name}' is not registered.");

        var runtimeOptions = new ChatClientAgentOptions
        {
            Name = configuredOptions.Name,
            Description = configuredOptions.Description,
            UseProvidedChatClientAsIs = configuredOptions.UseProvidedChatClientAsIs,
            ClearOnChatHistoryProviderConflict = configuredOptions.ClearOnChatHistoryProviderConflict,
            WarnOnChatHistoryProviderConflict = configuredOptions.WarnOnChatHistoryProviderConflict,
            ThrowOnChatHistoryProviderConflict = configuredOptions.ThrowOnChatHistoryProviderConflict,
            ChatOptions = configSection.GetSection("options").Get<ChatOptions>()
        };

        var providerName = client.GetService<ChatClientMetadata>()?.ProviderName;

        if (runtimeOptions.ChatOptions?.ModelId is null && !string.IsNullOrEmpty(configuredOptions.Model))
            (runtimeOptions.ChatOptions ??= new()).ModelId = configuredOptions.Model;

        if (!string.IsNullOrWhiteSpace(configuredOptions.Instructions))
            (runtimeOptions.ChatOptions ??= new()).Instructions = configuredOptions.Instructions;

        var providers = ResolveAIContextProviders(configSection, configuredOptions);
        if (providers.Count > 0)
            runtimeOptions.AIContextProviders = providers;

        runtimeOptions.ChatHistoryProvider ??=
            services.GetKeyedService<ChatHistoryProvider>(name) ??
            services.GetService<ChatHistoryProvider>();

        configure?.Invoke(name, runtimeOptions);

        LogConfigured(name);

        var agent = new ChatClientAgent(client, runtimeOptions, services.GetService<ILoggerFactory>(), services);
        var metadata = agent.GetService<AIAgentMetadata>() ?? new AIAgentMetadata(providerName);

        return new RuntimeState(
            agent, metadata, runtimeOptions, client,
            configuredOptions, new ConfigurableAgentMetadata(name, section, metadata.ProviderName));
    }

    List<AIContextProvider> ResolveAIContextProviders(IConfigurationSection configSection, ConfigurableAgentOptions configured)
    {
        var providers = new List<AIContextProvider>();
        var added = new HashSet<object>();

        // First go DI provided with same agent name.
        if (services.GetKeyedService<AIContext>(name) is { } agentContext)
        {
            providers.Add(new StaticContextProvider(agentContext, name));
            added.Add(agentContext);
        }

        if (services.GetKeyedService<AIContextProvider>(name) is { } agentProvider)
        {
            providers.Add(new DynamicContextProvider(agentProvider, name));
            added.Add(agentProvider);
        }

        foreach (var use in configured.Use ?? [])
        {
            var found = false;

            if (services.GetKeyedService<AIContext>(use) is { } useContext)
            {
                // Don't add the same provider twice.
                if (added.Contains(useContext))
                {
                    // The Use order prevails, though.
                    providers.RemoveAll(x => x is StaticContextProvider ctx && ctx.Context == useContext);
                }

                providers.Add(new StaticContextProvider(useContext, $"{section}:use<{use}>"));
                added.Add(useContext);
                found = true;
            }

            if (services.GetKeyedService<AIContextProvider>(use) is { } useProvider)
            {
                // Don't add the same provider twice.
                if (added.Contains(useProvider))
                {
                    // The Use order prevails, though.
                    providers.RemoveAll(x => x is DynamicContextProvider ctx && ctx.Provider == useProvider);
                }

                providers.Add(new DynamicContextProvider(useProvider, $"{section}:use<{use}>"));
                added.Add(useProvider);
                found = true;
            }

            if (configuration.GetSection("ai:context:" + use) is { } ctxSection &&
                ctxSection.Get<AIContextConfiguration>() is { } ctxConfig)
            {
                var configuredContext = new AIContext();

                if (ctxConfig.Instructions is not null)
                    configuredContext.Instructions = ctxConfig.Instructions.Dedent();

                if (ctxConfig.Messages is { Count: > 0 } messages)
                    configuredContext.Messages = messages;

                if (ctxConfig.Tools is not null)
                {
                    configuredContext.Tools = [.. ctxConfig.Tools
                        .Select(name => services.GetKeyedService<AITool>(name)
                            ?? services.GetKeyedService<AIFunction>(name)
                            ?? throw new InvalidOperationException($"Specified tool '{name}' for AI context '{ctxSection.Path}:tools' is not registered as a keyed {nameof(AITool)} or {nameof(AIFunction)}, and is required by agent section '{configSection.Path}'."))
                        ];
                }

                providers.Add(new StaticContextProvider(configuredContext, ctxSection.Path));
                found = true;
            }

            if (!found)
                throw new InvalidOperationException($"Specified AI context '{use}' for agent '{name}' is not registered as either {nameof(AIContext)} or configuration section 'ai:context:{use}'.");
        }

        if (configured.Tools != null)
        {
            List<AITool> tools = [.. configured.Tools
                .Select(name => services.GetKeyedService<AITool>(name)
                    ?? services.GetKeyedService<AIFunction>(name)
                    ?? throw new InvalidOperationException($"Specified tool '{name}' for AI context '{section}:tools' is not registered as a keyed {nameof(AITool)} or {nameof(AIFunction)}, and is required by agent section '{configSection.Path}'."))
                ];

            if (tools.Count > 0)
                providers.Add(new StaticContextProvider(new AIContext { Tools = tools }, $"{section}[tools]", true) { });
        }

        if (configured.Skills is { Count: > 0 })
        {
            foreach (var skill in configured.Skills)
            {
                if (skill != "*")
                    throw new InvalidOperationException($"Invalid value '{skill}' in '{section}:skills'. The only supported value is '*'.");
            }

            providers.Add(new FileAgentSkillsProvider(
                Path.Combine(AppContext.BaseDirectory, "skills"),
                options: null,
                services.GetService<ILoggerFactory>()));
        }

        return providers;
    }

    void OnReload(object? state)
    {
        var configSection = configuration.GetRequiredSection(section);

        (runtime.ChatClient as IDisposable)?.Dispose();
        reloadToken.Dispose();

        runtime = Configure(configSection);
        reloadToken = configuration.GetReloadToken().RegisterChangeCallback(OnReload, state: null);
    }

    [LoggerMessage(LogLevel.Information, "AIAgent '{Id}' configured.")]
    private partial void LogConfigured(string id);

    internal sealed class ConfigurableAgentOptions
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        //[JsonIgnore]
        public string? Instructions { get; set; }
        public string? Client { get; set; }
        //[JsonIgnore]
        public string? Model { get; set; }
        public IList<string>? Use { get; set; }
        public IList<string>? Skills { get; set; }
        public IList<string>? Tools { get; set; }
        public bool UseProvidedChatClientAsIs { get; set; }
        public bool ClearOnChatHistoryProviderConflict { get; set; }
        public bool WarnOnChatHistoryProviderConflict { get; set; }
        public bool ThrowOnChatHistoryProviderConflict { get; set; }
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.General,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
        )]
    [JsonSerializable(typeof(ConfigurableAgentOptions))]
    partial class JsonContext : JsonSerializerContext
    {
        static readonly Lazy<JsonSerializerOptions> options = new(CreateDefaultOptions);

        /// <summary>
        /// Provides a pre-configured instance of <see cref="JsonSerializerOptions"/> that aligns with the context's settings.
        /// </summary>
        public static JsonSerializerOptions DefaultOptions { get => options.Value; }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled")]
        static JsonSerializerOptions CreateDefaultOptions()
        {
            JsonSerializerOptions options = new(Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            };

            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    IgnoreEmptyCollections()
                }
            });

            options.MakeReadOnly();
            return options;
        }

        static Action<JsonTypeInfo> IgnoreEmptyCollections()
        {
            return typeInfo =>
            {
                if (typeInfo.Kind != JsonTypeInfoKind.Object)
                    return;

                foreach (var property in typeInfo.Properties)
                {
                    if (property.PropertyType == typeof(string) ||
                        !typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                    {
                        continue;
                    }

                    var shouldSerialize = property.ShouldSerialize;
                    property.ShouldSerialize = (obj, value) =>
                        value is not null &&
                        (shouldSerialize?.Invoke(obj, value) ?? true) &&
                        !IsEmptyCollection(value);
                }
            };

            static bool IsEmptyCollection(object value)
            {
                if (value is string)
                    return false;

                if (value is ICollection collection)
                    return collection.Count == 0;

                var enumerator = ((IEnumerable)value).GetEnumerator();
                try
                {
                    return !enumerator.MoveNext();
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
            }
        }
    }

    sealed record RuntimeState(
        ChatClientAgent Agent, AIAgentMetadata AgentMetadata, ChatClientAgentOptions AgentOptions, IChatClient ChatClient,
        ConfigurableAgentOptions ConfigurableOptions, ConfigurableAgentMetadata ConfigurableMetadata)
    {
        readonly Lazy<JsonElement> json = new(() => JsonSerializer.SerializeToElement(ConfigurableOptions, JsonContext.DefaultOptions));

        public JsonElement ConfiguredJson => json.Value;
    }
}

/// <summary>Metadata for a <see cref="ConfigurableAgent"/>.</summary>
[DebuggerDisplay("Name = {Name}, Provider = {Provider}, Section = {Section}")]
public sealed class ConfigurableAgentMetadata(string name, string section, string? provider)
{
    /// <summary>Name of the agent.</summary>
    public string Name => name;
    /// <summary>Name of the configured model provider.</summary>
    public string? Provider => provider;
    /// <summary>Configuration section where the agent is defined.</summary>
    public string Section => section;
}

class AIContextConfiguration
{
    public string? Instructions { get; set; }

    public IList<ChatMessage>? Messages =>
        MessageConfigurations?.Select(config =>
            config.System is not null ? Chat.System(config.System) :
            config.Developer is not null ? Chat.Developer(config.Developer) :
            config.User is not null ? Chat.User(config.User) :
            config.Assistant is not null ? Chat.Assistant(config.Assistant) :
            null).Where(x => x is not null).Cast<ChatMessage>().ToList();

    public IList<string>? Tools { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [ConfigurationKeyName("Messages")]
    public MessageConfiguration[]? MessageConfigurations { get; set; }
}

record MessageConfiguration(string? System = default, string? Developer = default, string? User = default, string? Assistant = default);

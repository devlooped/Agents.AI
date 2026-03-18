using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Devlooped.Agents.AI;
using Devlooped.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Json;

public static class ConsoleExtensions
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        AllowDuplicateProperties = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                IgnorePropertiesOfType<Delegate>(),
                IgnorePropertiesOfType<MethodInfo>(),
                IgnorePropertiesOfType<JsonSerializerOptions>(),
                IgnoreEmptyCollections(),
                info =>
                {
                    if (info.Kind != JsonTypeInfoKind.Object)
                        return;

                    foreach (var property in info.Properties)
                        if (property.Name == nameof(AIAgent.Id))
                            property.ShouldSerialize = (_, _) => false;
                }
            }
        },
        Converters =
        {
            new AIContextProviderJsonConverterFactory(),
            new AIToolJsonConverterFactory(),
            new ChatClientAgentOptionsJsonConverterFactory(),
            new ChatOptionsJsonConverterFactory()
        }
    };

    extension(IServiceProvider services)
    {
        public async ValueTask RenderAgentsAsync(IServiceCollection collection)
        {
            foreach (var description in collection.AsEnumerable().Where(x => x.ServiceType == typeof(IChatClient) && x.IsKeyedService && x.ServiceKey is string))
            {
                var client = services.GetKeyedService<IChatClient>(description.ServiceKey);
                if (client is null)
                    continue;

                var metadata = client.GetService<ConfigurableChatClientMetadata>();
                var chatopt = client.GetService<object>("Options");
                var json = JsonSerializer.Serialize(new { Metadata = metadata, Options = chatopt }, JsonOptions);
                AnsiConsole.Write(new Panel(new JsonText(json))
                {
                    Header = new PanelHeader($"| 💬 {metadata?.Id} from {metadata?.ConfigurationSection} |"),
                });
            }

            foreach (var name in collection.AsEnumerable()
                .Where(x => x.ServiceType == typeof(AIAgent) && x.IsKeyedService && x.ServiceKey is string)
                .Select(x => (string)x.ServiceKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var agent = services.GetRequiredKeyedService<AIAgent>(name);
                var metadata = agent.GetService<ConfigurableAgentMetadata>();
                var agentJson = agent is ConfigurableAgent configured ?
                    Merge(configured.Configuration, JsonSerializer.SerializeToElement(configured, JsonOptions)) :
                    (JsonObject)JsonSerializer.SerializeToNode(agent, JsonOptions)!;

                agentJson!["Metadata"] = JsonSerializer.SerializeToNode(metadata, JsonOptions);

                if (agentJson.Remove("Use", out var use))
                    agentJson.Add("Use", use);
                if (agentJson.Remove("Tools", out var tools))
                    agentJson.Add("Tools", tools);

                var json = JsonSerializer.Serialize(agentJson, JsonOptions);

                AnsiConsole.Write(new Panel(new JsonText(json))
                {
                    Header = new PanelHeader($"| 🤖 {agent.Name ?? metadata?.Name ?? "(unnamed)"} from {metadata?.Section} |"),
                });
            }

            await ValueTask.CompletedTask;
        }
    }

    static Action<JsonTypeInfo> IgnorePropertiesOfType<TIgnore>()
    {
        return typeInfo =>
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                var prop = typeInfo.Properties[i];

                if (typeof(TIgnore).IsAssignableFrom(prop.PropertyType))
                {
                    typeInfo.Properties.RemoveAt(i);
                    continue;
                }
            }
        };
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

    static JsonObject Merge(JsonElement target, JsonElement source)
    {
        var merged = JsonObject.Create(target)!;

        foreach (var property in source.EnumerateObject())
            if (!merged.ContainsKey(property.Name))
                merged[property.Name] = property.Value.Deserialize<JsonNode>();

        return merged;
    }

    sealed class AIContextProviderJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(AIContextProvider).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new AIContextProviderJsonConverter();

        class AIContextProviderJsonConverter : JsonConverter<AIContextProvider>
        {
            JsonSerializerOptions? innerOptions;

            public override AIContextProvider? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, AIContextProvider value, JsonSerializerOptions options)
            {
                innerOptions ??= CreateInnerOptions(options);

                var id = string.Join('|', value.StateKeys).Replace("AIContext-", "").Replace("AIContextProvider-", "");

                if (value is StaticContextProvider provider)
                {
                    var json = JsonSerializer.SerializeToNode(provider.Context, innerOptions);
                    if (json is JsonObject node)
                    {
                        node.Remove(nameof(AIContextProvider.StateKeys));
                        node.Insert(0, "Key", id);
                    }
                    JsonSerializer.Serialize(writer, json, options);
                }
                else
                {
                    var json = JsonSerializer.SerializeToNode(value, innerOptions);
                    if (json is JsonObject node)
                    {
                        node.Remove(nameof(AIContextProvider.StateKeys));
                        node.Insert(0, "Key", id);
                    }
                    JsonSerializer.Serialize(writer, json, options);
                }
            }

            static JsonSerializerOptions CreateInnerOptions(JsonSerializerOptions options)
            {
                var inner = new JsonSerializerOptions(options);
                for (var i = inner.Converters.Count - 1; i >= 0; i--)
                    if (inner.Converters[i] is AIContextProviderJsonConverterFactory)
                        inner.Converters.RemoveAt(i);
                return inner;
            }
        }
    }

    sealed class AIToolJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(IEnumerable<AITool>).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new AIToolJsonConverter();

        class AIToolJsonConverter : JsonConverter<IEnumerable<AITool>>
        {
            public override IEnumerable<AITool>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, IEnumerable<AITool> tools, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                foreach (var tool in tools)
                {
                    writer.WritePropertyName(tool.Name);
                    writer.WriteStringValue(tool.Description ?? "");
                }
                writer.WriteEndObject();
            }
        }
    }

    sealed class ChatClientAgentOptionsJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(ChatClientAgentOptions).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new ChatClientAgentOptionsJsonConverter();

        class ChatClientAgentOptionsJsonConverter : JsonConverter<ChatClientAgentOptions>
        {
            JsonSerializerOptions? innerOptions;

            public override ChatClientAgentOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, ChatClientAgentOptions value, JsonSerializerOptions options)
            {
                innerOptions ??= CreateInnerOptions(options);

                var node = JsonSerializer.SerializeToNode(value, innerOptions);
                if (node is JsonObject obj)
                {
                    obj.Remove("Name");
                    obj.Remove("Description");
                }

                node?.WriteTo(writer);
            }

            static JsonSerializerOptions CreateInnerOptions(JsonSerializerOptions options)
            {
                var inner = new JsonSerializerOptions(options);
                for (var i = inner.Converters.Count - 1; i >= 0; i--)
                    if (inner.Converters[i] is ChatClientAgentOptionsJsonConverterFactory)
                        inner.Converters.RemoveAt(i);
                return inner;
            }
        }
    }

    sealed class ChatOptionsJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(ChatOptions).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new ChatOptionsJsonConverter();

        class ChatOptionsJsonConverter : JsonConverter<ChatOptions>
        {
            JsonSerializerOptions? innerOptions;

            public override ChatOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, ChatOptions value, JsonSerializerOptions options)
            {
                innerOptions ??= CreateInnerOptions(options);

                var node = JsonSerializer.SerializeToNode(value, innerOptions);
                if (node is JsonObject obj)
                    obj.Remove("Instructions");

                node?.WriteTo(writer);
            }

            static JsonSerializerOptions CreateInnerOptions(JsonSerializerOptions options)
            {
                var inner = new JsonSerializerOptions(options);
                for (var i = inner.Converters.Count - 1; i >= 0; i--)
                    if (inner.Converters[i] is ChatOptionsJsonConverterFactory)
                        inner.Converters.RemoveAt(i);
                return inner;
            }
        }
    }
}

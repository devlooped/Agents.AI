using System.ClientModel.Primitives;
using System.Text.Json;

namespace Devlooped.Agents.AI;

public static class PipelineOutput
{
    extension<TOptions>(TOptions) where TOptions : ClientPipelineOptions, new()
    {
        public static TOptions WriteTo(ITestOutputHelper output)
            => new TOptions().WriteTo(output);
    }
}

public static class PipelineOutputExtensions
{
    static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
    };

    extension<TOptions>(TOptions options) where TOptions : ClientPipelineOptions
    {
        public TOptions WriteTo(ITestOutputHelper output)
            // Newer System.ClientModel versions no longer expose the previous Observe helper.
            // Keep the test-friendly API shape until these diagnostics are needed again.
            => options;
    }
}

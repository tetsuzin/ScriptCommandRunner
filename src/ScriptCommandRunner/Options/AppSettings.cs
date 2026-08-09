using System.Text.Json.Serialization;

namespace ScriptCommandRunner.Options;

internal sealed class AppSettings
{
    public const string FileName = "appsettings.json";

    public ScriptCommandRunnerOptions ScriptCommandRunnerOptions { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonSerializerContext : JsonSerializerContext;

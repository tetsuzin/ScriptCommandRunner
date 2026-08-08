using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ScriptCommandRunner.Options;

internal sealed class ScriptCommandRunnerOptions
{
    public const string DefaultScriptDirectory = "./";
    public const string DefaultExecutable = "bash";
    public const string DefaultScriptExtension = ".sh";

    public string[] ScriptDirectory { get; set; } = [];

    public string Executable { get; set; } = DefaultExecutable;

    public string[] ExecutableArguments { get; set; } = [];

    public string ScriptExtension { get; set; } = DefaultScriptExtension;
}

internal static class ScriptCommandRunnerOptionsExtensions
{
    internal static ConsoleApp.ConsoleAppBuilder ConfigureServices(
        this ConsoleApp.ConsoleAppBuilder builder)
    {
        return builder.ConfigureServices(ConfigureScriptCommandRunnerServices);
    }

    private static void ConfigureScriptCommandRunnerServices(
        IConfiguration configuration,
        IServiceCollection services)
    {
        var options = new ScriptCommandRunnerOptions();
        configuration
            .GetSection(nameof(ScriptCommandRunnerOptions))
            .Bind(options);

        options.ScriptDirectory = options.ScriptDirectory is null or []
            ? [ScriptCommandRunnerOptions.DefaultScriptDirectory]
            : options.ScriptDirectory;
        options.ExecutableArguments ??= [];

        if (string.IsNullOrWhiteSpace(options.Executable))
        {
            throw new InvalidOperationException(
                $"{nameof(ScriptCommandRunnerOptions)}:{nameof(options.Executable)} must not be empty.");
        }

        if (options.ScriptDirectory.Any(string.IsNullOrWhiteSpace))
        {
            var configurationKey =
                $"{nameof(ScriptCommandRunnerOptions)}:{nameof(options.ScriptDirectory)}";
            throw new InvalidOperationException(
                $"{configurationKey} must not contain empty values.");
        }

        if (options.ScriptExtension is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ScriptCommandRunnerOptions)}:{nameof(options.ScriptExtension)} must not be null.");
        }

        services.AddSingleton(options);
    }
}

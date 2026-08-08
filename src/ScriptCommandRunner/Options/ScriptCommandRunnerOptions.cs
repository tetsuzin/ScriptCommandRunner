using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ScriptCommandRunner.Options;

internal sealed class ScriptCommandRunnerOptions
{
    public const string DefaultScriptDirectory = "./";
    public const string DefaultExecutable = "bash";
    public const string DefaultScriptExtension = ".sh";

    // The configuration binder appends to arrays instead of replacing them,
    // so the default is applied after binding rather than in this initializer.
    public string[] ScriptDirectory { get; set; } = [];

    public string Executable { get; set; } = DefaultExecutable;

    public string[] ExecutableArguments { get; set; } = [];

    public string ScriptExtension { get; set; } = DefaultScriptExtension;

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Executable))
        {
            return $"{nameof(ScriptCommandRunnerOptions)}:{nameof(Executable)} must not be empty.";
        }

        if (ScriptDirectory.Any(string.IsNullOrWhiteSpace))
        {
            return $"{nameof(ScriptCommandRunnerOptions)}:{nameof(ScriptDirectory)} must not contain empty values.";
        }

        if (ExecutableArguments.Any(argument => argument is null))
        {
            return $"{nameof(ScriptCommandRunnerOptions)}:{nameof(ExecutableArguments)} must not contain null values.";
        }

        if (string.IsNullOrWhiteSpace(ScriptExtension))
        {
            return $"{nameof(ScriptCommandRunnerOptions)}:{nameof(ScriptExtension)} must not be empty.";
        }

        return null;
    }
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

        if (options.ScriptDirectory is [])
        {
            options.ScriptDirectory = [ScriptCommandRunnerOptions.DefaultScriptDirectory];
        }

        services.AddSingleton(options);
    }
}

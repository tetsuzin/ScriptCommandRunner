using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ScriptCommandRunner.Options;

internal sealed class ScriptCommandRunnerOptions
{
    public const string DefaultScriptDirectory = "./";

    public string[] ScriptDirectory { get; set; } = [];

    public string Executable { get; set; } = "bash";

    public string[] ExecutableArguments { get; set; } = [];

    public string ScriptExtension { get; set; } = ".sh";
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

        options.ScriptDirectory = options.ScriptDirectory is []
            ? [ScriptCommandRunnerOptions.DefaultScriptDirectory]
            : options.ScriptDirectory;

        services.AddSingleton(options);
    }
}

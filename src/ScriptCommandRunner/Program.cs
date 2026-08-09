using System.Text.Json;
using ConsoleAppFramework;
using ScriptCommandRunner;
using ScriptCommandRunner.Options;

try
{
    // Skip loading the file for --init so a corrupted appsettings.json
    // can still be regenerated with --init --force.
    var options = args is ["--init", ..]
        ? new ScriptCommandRunnerOptions()
        : LoadOptions();
    var commands = new ScriptCommands(options);

    var app = ConsoleApp.Create();
    app.Add("--init", commands.Init);
    app.Add("", commands.Run);
    await app.RunAsync(NormalizeArguments(args));
}
catch (Exception exception) when (exception is JsonException or IOException)
{
    Console.Error.WriteLine($"Failed to load {AppSettings.FileName}: {exception.Message}");
    Environment.ExitCode = 1;
}

static ScriptCommandRunnerOptions LoadOptions()
{
    var appSettingsPath = Path.Combine(AppContext.BaseDirectory, AppSettings.FileName);

    if (!File.Exists(appSettingsPath))
    {
        return new ScriptCommandRunnerOptions();
    }

    using var stream = File.OpenRead(appSettingsPath);
    var context = AppSettingsJsonSerializerContext.Default.AppSettings;
    var appSettings = JsonSerializer.Deserialize(stream, context) ?? new AppSettings();
    var options = appSettings.ScriptCommandRunnerOptions ?? new ScriptCommandRunnerOptions();

    // Explicit JSON nulls can overwrite the property defaults.
    if (options.ScriptDirectory is null)
    {
        options.ScriptDirectory = ScriptCommandRunnerOptions.DefaultScriptDirectory;
    }

    if (options.ExecutableArguments is null)
    {
        options.ExecutableArguments = [];
    }

    return options;
}

static string[] NormalizeArguments(string[] arguments)
{
    return arguments switch
    {
        ["--init", ..] or [_, "--", ..] => arguments,
        [var command, var subCommand, .. var remaining] => [command, "--", subCommand, .. remaining],
        _ => arguments,
    };
}

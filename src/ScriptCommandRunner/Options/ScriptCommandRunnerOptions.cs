namespace ScriptCommandRunner.Options;

internal sealed class ScriptCommandRunnerOptions
{
    public const string DefaultScriptDirectory = "./";
    public const string DefaultExecutable = "bash";
    public const string DefaultScriptExtension = ".sh";

    public string[] ScriptDirectory { get; set; } = [DefaultScriptDirectory];

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

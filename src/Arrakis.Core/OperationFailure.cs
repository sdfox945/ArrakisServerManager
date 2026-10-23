namespace Arrakis.Core;

public enum ConfigurationFileState { VerifiedUnchanged, VerifiedUpdated, VerifiedExternalChange, Uncertain }

public sealed record OperationFailure(
    string File,
    string Step,
    string Explanation,
    string UnderlyingError,
    ConfigurationFileState FileState,
    string Recovery,
    string TechnicalDetails)
{
    public string StateDescription => FileState switch
    {
        ConfigurationFileState.VerifiedUnchanged => "Verified unchanged from the loaded original",
        ConfigurationFileState.VerifiedUpdated => "Verified updated to the reviewed proposal",
        ConfigurationFileState.VerifiedExternalChange => "Verified external change; no replacement by this save",
        _ => "Uncertain; manual review required"
    };
    public string Summary => $"{File} — {Step} failed.\n{Explanation}\nLive file: {StateDescription}.\nError: {FirstLine(UnderlyingError)}\nNext: {Recovery}";
    public string Details => Redactor.Clean(Summary + "\n\nTechnical details\n" + TechnicalDetails);
    private static string FirstLine(string text) => Redactor.Clean(text).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "No additional error text was returned.";
}

public interface IOperationFailure { OperationFailure Failure { get; } }
public sealed class ConfigurationSaveException(OperationFailure failure) : InvalidOperationException(failure.Summary), IOperationFailure
{
    public OperationFailure Failure { get; } = failure;
}
public sealed class ConfigurationSaveUnknownException(OperationFailure failure) : OutcomeUnknownException(failure.Summary), IOperationFailure
{
    public OperationFailure Failure { get; } = failure;
}

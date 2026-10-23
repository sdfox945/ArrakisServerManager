namespace Arrakis.Core;

public interface IServerGateway
{
    bool IsDemo { get; }
    Task<ServerSnapshot> RefreshAsync(CancellationToken token);
    Task<ActionResult> ExecuteAsync(ServerAction action, string? argument, IProgress<string>? progress, CancellationToken token);
    Task<IReadOnlyList<ConfigFile>> LoadConfigurationAsync(CancellationToken token);
    Task<ConfigFile> SaveConfigurationAsync(ConfigFile original, byte[] proposed, CancellationToken token);
    Task<ConfigFile> SaveReviewedConfigurationAsync(ConfigurationReview review, CancellationToken token)
    {
        review.Validate();
        return SaveConfigurationAsync(review.Original, review.Payload, token);
    }
    Task<IReadOnlyList<BackupArtifact>> ListBackupsAsync(CancellationToken token);
    Task<string> DownloadBackupAsync(BackupArtifact artifact, CancellationToken token);
    Task<string> ReadLogsAsync(string source, MapInstance? pod, CancellationToken token);
    Task<string> ChangeMapAsync(MapChange change, CancellationToken token);
    Task<string> CheckUpdatesAsync(CancellationToken token);
    Task<Uri> GetToolUriAsync(bool director, CancellationToken token);
    Task LaunchOfficialEditorAsync(CancellationToken token);
    Task<string> LaunchMaintenanceAsync(MaintenanceAction action,CancellationToken token);
}
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, IProgress<string>? progress, CancellationToken token);
}
public record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout, string? Input = null, string? BinaryOutput = null, int OutputLimit = 120000);
public record ProcessResult(int ExitCode, string Output, string Error)
{
    public void EnsureSuccess() { if (ExitCode != 0) throw new InvalidOperationException($"Command exited {ExitCode}: {Redactor.Clean(Error.Length > 0 ? Error : Output)}"); }
}
public interface IPreferencesStore { Preferences Load(); void Save(Preferences preferences); }
public interface IHistoryStore { IReadOnlyList<OperationRecord> Load(); void Append(OperationRecord record); }
public record OperationRecord(DateTimeOffset Timestamp, string Name, string Outcome, string Output, string Details = "");
public class OutcomeUnknownException(string message, Exception? inner = null) : Exception(message, inner);

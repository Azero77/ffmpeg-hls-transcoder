namespace App.Models;

/// <summary>
/// Structured exit context logged at process termination.
/// Enables CloudWatch Insights queries: filter @message like /Stage=Download/
/// </summary>
public sealed record ExitReason(
    ExitCode Code,
    string Stage,
    string Message,
    Exception? Exception = null)
{
    public static ExitReason Ok() =>
        new(ExitCode.Success, "Complete", "Pipeline finished successfully.");

    public static ExitReason InvalidInput(string message) =>
        new(ExitCode.InvalidInput, "InputValidation", message);

    public static ExitReason Cancelled(string stage) =>
        new(ExitCode.Cancelled, stage, "Operation cancelled (SIGTERM).");

    public static ExitReason Failure(string stage, Exception ex) =>
        new(ExitCode.Failed, stage, ex.Message, ex);
}

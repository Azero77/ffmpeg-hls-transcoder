namespace App.Models;

/// <summary>
/// Process exit codes matching the SFN contract:
/// 0 → step succeeds; non-zero → States.TaskFailed → Catch.
/// </summary>
public enum ExitCode
{
    /// <summary>All pipeline stages completed successfully.</summary>
    Success = 0,

    /// <summary>Unhandled exception during pipeline execution.</summary>
    Failed = 1,

    /// <summary>SIGTERM / graceful cancellation (Fargate task stop).</summary>
    Cancelled = 2,

    /// <summary>Job input JSON is malformed or fails validation.</summary>
    InvalidInput = 3
}

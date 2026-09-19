namespace App.Models;

/// <summary>
/// Validates a <see cref="TranscodingJobInput"/> at load time.
/// Returns a list of validation errors; empty list = valid.
/// </summary>
public static class TranscodingJobInputValidator
{
    public static IReadOnlyList<string> Validate(TranscodingJobInput input)
    {
        var errors = new List<string>();

        if (input.VideoId == Guid.Empty)
            errors.Add("VideoId is required.");
        if (input.TenantId == Guid.Empty)
            errors.Add("TenantId is required.");
        if (string.IsNullOrWhiteSpace(input.SourcePath))
            errors.Add("SourcePath is required.");
        if (string.IsNullOrWhiteSpace(input.OutputPrefix))
            errors.Add("OutputPrefix is required.");

        if (input.SourceMetadata is null)
        {
            errors.Add("SourceMetadata is required.");
        }
        else
        {
            if (input.SourceMetadata.SourceWidth <= 0 || input.SourceMetadata.SourceHeight <= 0)
                errors.Add("SourceMetadata dimensions must be positive.");
            if (input.SourceMetadata.Duration <= TimeSpan.Zero)
                errors.Add("SourceMetadata.Duration must be positive.");
        }

        if (input.Settings?.Outputs is not { Length: > 0 })
        {
            errors.Add("Settings.Outputs must contain at least one preset.");
        }
        else
        {
            foreach (var preset in input.Settings.Outputs)
            {
                if (preset.Width <= 0 || preset.Height <= 0)
                    errors.Add($"OutputPreset '{preset.NameModifier}': dimensions must be positive.");
                if (preset.MaxBitrateKbps <= 0)
                    errors.Add($"OutputPreset '{preset.NameModifier}': MaxBitrateKbps must be positive.");
            }
        }

        if (input.EncryptionMethod != EncryptionMethod.None)
        {
            if (input.Encryption is null)
            {
                errors.Add("Encryption settings required when EncryptionMethod is not None.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.Encryption.KeyId))
                    errors.Add("Encryption.KeyId is required.");
                if (string.IsNullOrWhiteSpace(input.Encryption.Key))
                    errors.Add("Encryption.Key is required.");
            }
        }

        return errors;
    }
}

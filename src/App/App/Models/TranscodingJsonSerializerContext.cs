using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Models;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(TranscodingJobInput))]
[JsonSerializable(typeof(TranscoderOptions))]
public partial class TranscodingJsonSerializerContext : JsonSerializerContext
{
}

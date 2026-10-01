using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using App.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Pipeline;

public interface IProgressNotifier
{
    Task NotifyAsync(Guid tenantId, Guid videoId, string stage, string status);
}

public class ProgressMessage
{
    public string VideoId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Status { get; set; } = "";
}

[System.Text.Json.Serialization.JsonSerializable(typeof(ProgressMessage))]
public partial class ProgressMessageContext : System.Text.Json.Serialization.JsonSerializerContext { }

public class ProgressNotifier : IProgressNotifier
{
    private readonly IAmazonSQS _sqsClient;
    private readonly string _queueUrl;
    private readonly ILogger<ProgressNotifier> _logger;

    public ProgressNotifier(IAmazonSQS sqsClient, ILogger<ProgressNotifier> logger, IOptions<TranscoderOptions> options)
    {
        _sqsClient = sqsClient;
        _logger = logger;
        _queueUrl = options.Value.ProgressQueueUrl ?? throw new ArgumentException("Sqs Settings is not defined");
    }

    public async Task NotifyAsync(Guid tenantId, Guid videoId, string stage, string status)
    {

        var message = new ProgressMessage
        {
            VideoId = videoId.ToString(),
            TenantId = tenantId.ToString(),
            Stage = stage,
            Status = status
        };

        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = JsonSerializer.Serialize(message, ProgressMessageContext.Default.ProgressMessage)
        };

        try
        {
            await _sqsClient.SendMessageAsync(request);
            _logger.LogDebug("Sent progress for {Stage} {VideoId}", stage, videoId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send progress event to SQS.");
        }
    }
}
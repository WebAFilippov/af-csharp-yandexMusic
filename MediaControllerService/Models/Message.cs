using System.Text.Json.Serialization;

namespace MediaControllerService.Models;

public class Message
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public class CommandMessage
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("stepPercent")]
    public int? StepPercent { get; set; }

    [JsonPropertyName("value")]
    public int? Value { get; set; }
}

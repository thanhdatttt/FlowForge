using System.ComponentModel.DataAnnotations;

namespace FlowForge.Infrastructure.Ai;

public sealed class OllamaCloudOptions
{
    // Both remain empty until the user supplies their own Cloud key and exact API model name.
    [MaxLength(8192), RegularExpression(@"[^\r\n]*")]
    public string ApiKey { get; set; } = "";

    [MaxLength(200), RegularExpression(@"\S*")]
    public string Model { get; set; } = "";
}

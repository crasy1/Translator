using Newtonsoft.Json;

namespace Translator.dto;

public struct MessageDto
{
    /// <summary>
    /// 消息的角色system,user,assistant或tool
    /// </summary>
    [JsonProperty("role")]
    public string Role { set; get; }

    /// <summary>
    /// 消息的内容
    /// </summary>
    [JsonProperty("content")]
    public string Content { set; get; }

    /// <summary>
    /// (可选）：要包含在消息中的图像列表（对于多模态模型，例如llava)
    /// </summary>
    [JsonProperty("images")]
    public string Images { set; get; }

    public static MessageDto System(string content)
    {
        return new MessageDto
        {
            Role = "system",
            Content = content
        };
    }

    public static MessageDto User(string content)
    {
        return new MessageDto
        {
            Role = "user",
            Content = content
        };
    }

    public static MessageDto Assistant(string content)
    {
        return new MessageDto
        {
            Role = "assistant",
            Content = content
        };
    }
}
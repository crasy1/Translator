using System.Collections.Generic;
using Newtonsoft.Json;

namespace Translator.dto;

public struct ChatResultDto
{
    /// <summary>
    /// 模型名称
    /// </summary>
    [JsonProperty("model")]
    public string Model { set; get; }

    /// <summary>
    /// 聊天的消息，可用于保存聊天记录
    /// </summary>
    [JsonProperty("message")]
    public MessageDto Message { set; get; }
}
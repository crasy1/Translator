using System.Collections.Generic;
using Newtonsoft.Json;

namespace Translator.dto;

public struct ChatParamDto
{
    /// <summary>
    /// （必需）模型名称
    /// </summary>
    [JsonProperty("model")] 
    public string Model { set; get; }
    /// <summary>
    /// 聊天的消息，可用于保存聊天记录
    /// </summary>
    [JsonProperty("messages")] 
    public List<MessageDto> Messages { set; get; }
    /// <summary>
    /// 返回响应的格式。格式可以是json或 JSON 架构
    /// </summary>
    [JsonProperty("format")] 
    public string Format { set; get; }
    /// <summary>
    /// 如果false响应将作为单个响应对象返回，而不是对象流
    /// </summary>
    [JsonProperty("stream")] 
    public bool Stream { set; get; }
}
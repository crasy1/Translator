using System.Collections.Generic;
using Newtonsoft.Json;

namespace Translator.dto;

public struct OpenAiChatResultDto
{
    [JsonProperty("id")] public string Id { set; get; }
    [JsonProperty("object")] public string Object { set; get; }
    [JsonProperty("created")] public long Created { set; get; }
    [JsonProperty("model")] public string Model { set; get; }
    [JsonProperty("system_fingerprint")] public string SystemFingerprint { set; get; }
    [JsonProperty("choices")] public List<ChoiceDto> Choices { set; get; }
    
}
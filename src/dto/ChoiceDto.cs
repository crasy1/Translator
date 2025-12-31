using System.Collections.Generic;
using Newtonsoft.Json;

namespace Translator.dto;

public struct ChoiceDto
{
    [JsonProperty("index")] public int Index { set; get; }
    [JsonProperty("message")] public MessageDto Message { set; get; }
}
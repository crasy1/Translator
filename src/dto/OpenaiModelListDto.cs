using System.Collections.Generic;
using Newtonsoft.Json;

namespace Translator.dto;

public class OpenaiModelListDto
{
    [JsonProperty("data")] public List<OpenaiModelDto> Data { get; set; }
}
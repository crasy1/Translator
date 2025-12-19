using Newtonsoft.Json;

namespace Translator.dto;

public class OpenaiModelDto
{
    [JsonProperty("id")]
    public string Id { get; set; }
}
using Newtonsoft.Json;

namespace Translator.dto;

public class ModelDto
{
    [JsonProperty("name")]
    public string Name { get; set; }
}
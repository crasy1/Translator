using System.Collections.Generic;
using Newtonsoft.Json;

namespace Translator.dto;

public class ModelListDto
{
    [JsonProperty("models")] public List<ModelDto> Models { get; set; }
}
using Godot.Collections;

namespace Translator.data;

public class LocaleData
{
    public static Dictionary<string, string> Language = new()
    {
        { "en", "英语" },
        { "zh_cn", "简体中文" },
        { "zh_tw", "繁体中文" },
        { "ja", "日语" },
        { "ko", "韩语" },
        { "fr", "法语" },
        { "de", "德语" },
        { "ru", "俄语" },
        { "es", "西班牙语" },
        { "pt_br", "巴西葡萄牙语" },
        { "cs", "捷克语" },
        { "it", "意大利语" },
        { "pl", "波兰语" },
        { "uk", "乌克兰语" },
    };
}
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NJsonSchema;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using Translator.api;
using Translator.data;
using Translator.dto;
using FileAccess = Godot.FileAccess;

[SceneTree]
public partial class Main : CanvasLayer
{
    private string SourceLang { set; get; }
    private List<string> TargetLangs = [];
    private string ModelName { set; get; }
    private List<Dictionary<string, string>> CsvCache { set; get; }

    private OllamaApiClient OllamaApiClient { set; get; }

    private string Extension { set; get; }
    private string SavePath { set; get; }

    private const string CsvKeys = "keys";

    private const string TemplatePrompt = @"您是一位专业的翻译人员。请准确地翻译给定的文本，同时保留原始的含义和语气。
翻译规则：
1.将{0}内容翻译成{1}。
2.仅返回翻译结果，不添加任何解释或额外的评论。
3.保持代码格式、变量名称（snake_case、camelCase）以及特殊字符的原有形式。
4.确保技术术语的准确性和原文的语气一致。
5.换行需要保持一致,标点符号需要保持一致
";


    public override async void _Ready()
    {
        InitLanguages();
        await InitOllama();
        GetWindow().FilesDropped += OnFileDropped;
    }

    /// <summary>
    /// 检测文件拖动
    /// </summary>
    /// <param name="files"></param>
    private void OnFileDropped(string[] files)
    {
        var filePath = files[0];
        List<string> extensions = ["pot", "po", "csv", "translation"];

        Extension = FileAccess.GetExtension(filePath);
        if (!extensions.Contains(Extension))
        {
            Log.Error($"文件格式错误 {Extension}");
            SourceFile.Text = null;
        }
        else
        {
            SourceFile.Text = filePath;
        }
    }

    private void BuildPrompt()
    {
        var list = TargetLangs.Select(l => LocaleData.Language[l]).ToList();
        var prompt = string.Format(TemplatePrompt, LocaleData.Language[SourceLang], string.Join(",", list));
        Prompt.Text = prompt;
    }


    /// <summary>
    ///   初始化语言
    /// </summary>
    private void InitLanguages()
    {
        Preview.Disabled = true;
        Save.Disabled = true;
        var buttonGroup = new ButtonGroup();
        foreach (var (code, lang) in LocaleData.Language)
        {
            var sBox = new CheckBox();
            sBox.Text = lang;
            Source.AddChild(sBox);
            sBox.ButtonGroup = buttonGroup;
            sBox.Toggled += (toggled) =>
            {
                if (toggled)
                {
                    SourceLang = code;
                    Log.Info("源语言：", SourceLang);
                    BuildPrompt();
                }
            };
            if (code == "en")
            {
                sBox.ButtonPressed = true;
            }

            var tBox = new CheckBox();
            tBox.Text = lang;
            Target.AddChild(tBox);
            tBox.Toggled += (toggled) =>
            {
                if (toggled)
                {
                    TargetLangs.Add(code);
                }
                else
                {
                    TargetLangs.Remove(code);
                }

                Log.Info("目标语言：", string.Join(",", TargetLangs));
                BuildPrompt();
            };
            if (code == "zh_cn")
            {
                tBox.ButtonPressed = true;
            }
        }

        Save.Pressed += () =>
        {
            SavePath = Path.Join(OS.GetUserDataDir(), "temp", $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.{Extension}");
            FileUtil.WriteCsv(SavePath, CsvCache);
            Log.Info($"保存 {SavePath}");
            SavedPath.Text = SavePath;
        };
        Open.Pressed += () =>
        {
            if (FileAccess.FileExists(SavePath))
            {
                OS.ShellOpen(FileAccess.GetParentDir(SavePath));
            }
        };
        Preview.Pressed += () =>
        {
            PreviewContainer.ClearAndFreeChildren();
            if (CsvCache is null)
            {
                return;
            }

            TranslationServer.Clear();
            var langKeys = new HashSet<string>(CsvCache[0].Keys.Where(k => !CsvKeys.Equals(k)));
            var translateKeys = CsvCache.Select(dictionary => dictionary[CsvKeys]).ToList();

            Log.Info($"翻译语言：{string.Join(",", langKeys)}");
            Log.Info($"翻译字段：{string.Join(",", translateKeys)}");
            Dictionary<string, Godot.Collections.Dictionary> translateMap = new();

            var header = new HBoxContainer();
            PreviewContainer.AddChild(header);
            foreach (var langKey in langKeys)
            {
                translateMap.TryAdd(langKey, new());
                var btn = new Button();
                btn.Text = langKey;
                header.AddChild(btn);
                btn.Pressed += () => { TranslationServer.SetLocale(langKey); };
            }

            for (var i = 0; i < translateKeys.Count; i++)
            {
                var label = new Label();
                label.Text = translateKeys[i];
                PreviewContainer.AddChild(label);
                foreach (var (lang, v) in CsvCache[i])
                {
                    if (!lang.Equals(CsvKeys))
                    {
                        translateMap[lang].Add(translateKeys[i], v);
                    }
                }
            }

            foreach (var (k, v) in translateMap)
            {
                var translation = new Translation();
                translation.Locale = k;
                translation.Messages = v;
                TranslationServer.AddTranslation(translation);
            }
        };
        Translate.Pressed += async () =>
        {
            Translate.Disabled = true;
            TranslateProgress.Value = 0;
            TranslateProgress.MaxValue = TargetLangs.Count;
            var sourceFilePath = SourceFile.Text;
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                Log.Error("没有设置源文件");
                return;
            }

            if (!File.Exists(sourceFilePath))
            {
                Log.Error("源文件不存在");
                return;
            }

            if (string.IsNullOrWhiteSpace(SourceLang))
            {
                Log.Error("没有设置源语言");
                return;
            }

            if (TargetLangs.Count <= 0)
            {
                Log.Error("没有设置目标语言");
                return;
            }

            if (!await OllamaApiClient.IsRunningAsync() | string.IsNullOrWhiteSpace(ModelName))
            {
                Log.Error("ollama未启动或者未选择模型");
                return;
            }

            if (sourceFilePath.EndsWith("csv"))
            {
                CsvCache = FileUtil.ReadCsv(sourceFilePath);
                var sourceTranslation = new StringBuilder();
                foreach (var row in CsvCache)
                {
                    if (row.TryGetValue(SourceLang, out var sourceText))
                    {
                        sourceTranslation.AppendLine(sourceText);
                    }
                    else
                    {
                        Log.Error($"没有找到 {LocaleData.Language[SourceLang]} 相关翻译");
                        return;
                    }
                }

                foreach (var targetLang in TargetLangs)
                {
                    var translateResult = await TranslateChat(SourceLang, targetLang, sourceTranslation.ToString());
                    // 处理换行
                    var targetValues =
                        translateResult.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries);
                    Log.Info($"源 {SourceLang} key 数量：{CsvCache.Count}，翻译 {targetLang} 后key 数量： {targetValues.Length}");
                    for (var i = 0; i < CsvCache.Count; i++)
                    {
                        CsvCache[i][targetLang] = targetValues[i];
                    }

                    TranslateProgress.Value += 1;
                    Log.Info($"progress: {TranslateProgress.Value}/{TranslateProgress.MaxValue}");
                }

                Preview.Disabled = false;
                Save.Disabled = false;
                Translate.Disabled = false;
                return;
            }

            if (sourceFilePath.EndsWith("translation"))
            {
                var translation = ResourceLoader.Load<Translation>(sourceFilePath);
                var messageList = translation.GetMessageList();
                var translatedMessageList = translation.GetTranslatedMessageList();
                var locale = translation.GetLocale();
                Preview.Disabled = false;
                Save.Disabled = false;
                Translate.Disabled = false;
                return;
            }
        };
    }

    public async Task<string> TranslateChat(string sourceLang, string targetLang, string content)
    {
        var prompt = string.Format(TemplatePrompt, LocaleData.Language[sourceLang],
            LocaleData.Language[targetLang]);
        var result = "";
        // TODO json
        var dict = new Dictionary<string, object>
        {
            {"name", "John"},
            {"age", 30},
            {"isActive", true}
        };
        var jsonSchema = JsonSchema.FromSampleJson(JsonUtil.ToJsonString(dict));

        var chatRequest = new ChatRequest()
        {
            Model = ModelName,
            Messages =
            [
                new Message(ChatRole.System, prompt),
                new Message(ChatRole.User, content)
            ],
        };
        await foreach (var chatResponseStream in OllamaApiClient.ChatAsync(chatRequest))
        {
            result += chatResponseStream.Message.Content;
        }

        Log.Info(result);
        return result;
    }

    /// <summary>
    /// 初始化ollama设置
    /// </summary>
    private async Task InitOllama()
    {
        var httpClient = new System.Net.Http.HttpClient()
        {
            BaseAddress = new(string.IsNullOrWhiteSpace(Host.Text) ? Host.PlaceholderText : Host.Text),
            Timeout = TimeSpan.FromSeconds(60)
        };
        OllamaApiClient = new OllamaApiClient(httpClient);
        try
        {
            if (!await OllamaApiClient.IsRunningAsync())
            {
                throw new Exception("ollama未安装或者未启动成功");
            }
        }
        catch (Exception e)
        {
            Ollama.Start();
        }

        try
        {
            if (!await OllamaApiClient.IsRunningAsync())
            {
                throw new Exception("ollama未安装或者未启动成功");
            }
        }
        catch (Exception e)
        {
            Log.Error("ollama未安装或者未启动成功");
            GetTree().Quit();
        }

        Log.Info(await OllamaApiClient.GetVersionAsync());

        Host.FocusExited += () =>
        {
            var httpClient = new System.Net.Http.HttpClient()
            {
                BaseAddress = new(string.IsNullOrWhiteSpace(Host.Text) ? Host.PlaceholderText : Host.Text),
                Timeout = TimeSpan.FromSeconds(60)
            };
            OllamaApiClient = new OllamaApiClient(httpClient);
        };
        
        ListModelBtn.Pressed += async () =>
        {
            var localModelList = await OllamaApiClient.ListLocalModelsAsync();
            var runningModelList = await OllamaApiClient.ListRunningModelsAsync();
            foreach (var modelDto in localModelList)
            {
                Log.Info($"{modelDto.Name} ,running: {runningModelList.Any(m => m.Name.Equals(modelDto.Name))}");
            }
        };
        Model.ItemSelected += async (index) =>
        {
            Model.SelfModulate = Colors.Red;
            ModelName = Model.GetItemText((int)index);
            var runningModelList = await OllamaApiClient.ListRunningModelsAsync();
            if (runningModelList.Any(m => m.Name == ModelName))
            {
                Model.SelfModulate = Colors.Green;
                return;
            }

            foreach (var modelDto in runningModelList)
            {
                OllamaApiClient.ChatAsync(new ChatRequest() { Model = modelDto.Name, KeepAlive = "0" });
            }

            OllamaApiClient.SelectedModel = ModelName;
            Log.Info($"加载模型 {ModelName}");
            Model.SelfModulate = Colors.Green;
        };

        Model.Clear();
        var runningModelListTask = OllamaApiClient.ListRunningModelsAsync();
        var localModelList = await OllamaApiClient.ListLocalModelsAsync();
        foreach (var modelDto in localModelList)
        {
            Log.Info(modelDto.Name);
            Model.AddItem(modelDto.Name);
        }

        var runningModelList = await runningModelListTask;
        if (runningModelList.Count() > 0)
        {
            var runningModelName = runningModelList.First().Name;
            var valueTuples = localModelList.Index().Where((im, i) => im.Item.Name.Equals(runningModelName))
                .GetEnumerator();
            Log.Info($"默认运行{runningModelName} - {valueTuples.Current.Index}");
            Model.SelectAndEmitSignal(valueTuples.Current.Index);
        }
        else
        {
            if (localModelList.Count() > 0)
            {
                Model.SelectAndEmitSignal(0);
            }
        }
    }
}
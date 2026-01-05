using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NJsonSchema;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using Translator.api;
using Translator.data;
using ChatRole = OllamaSharp.Models.Chat.ChatRole;
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
    private ButtonGroup SourceLangGroup = new();
    private ButtonGroup PreviewLangGroup = new();

    private string SystemPrompt { set; get; }


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
            Log.Error($"[导入] 文件格式错误 {Extension}");
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
        var prompt = string.Format(SystemPrompt, LocaleData.Language[SourceLang], string.Join(",", list));
        Prompt.Text = prompt;
    }


    /// <summary>
    ///   初始化语言
    /// </summary>
    private void InitLanguages()
    {
        PreviewBtn.Disabled = true;
        SaveBtn.Disabled = true;
        PreviewPromptBtn.Disabled = true;
        Prompt.Editable = false;
        SystemPrompt = Prompt.Text;

        foreach (var (code, lang) in LocaleData.Language)
        {
            var sBox = new CheckBox();
            sBox.Text = lang;
            Source.AddChild(sBox);
            sBox.ButtonGroup = SourceLangGroup;
            sBox.Toggled += (toggled) =>
            {
                if (toggled)
                {
                    SourceLang = code;
                    Log.Info("[翻译设置] 源语言：", SourceLang);
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

                Log.Info("[翻译设置] 目标语言：", string.Join(",", TargetLangs));
                BuildPrompt();
            };
            if (code == "zh_cn")
            {
                tBox.ButtonPressed = true;
            }
        }

        EditPromptBtn.Pressed += () =>
        {
            Prompt.Editable = true;
            Prompt.Text = SystemPrompt;
            PreviewPromptBtn.Disabled = false;
        };
        PreviewPromptBtn.Pressed += () =>
        {
            Prompt.Editable = false;
            SystemPrompt = Prompt.Text;
            BuildPrompt();
            PreviewPromptBtn.Disabled = true;
        };
        SaveBtn.Pressed += () =>
        {
            SavePath = Path.Join(OS.GetUserDataDir(), "temp", $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.{Extension}");
            FileUtil.WriteCsv(SavePath, CsvCache);
            Log.Info($"保存 {SavePath}");
            SavedPath.Text = SavePath;
        };
        OpenBtn.Pressed += () =>
        {
            if (FileAccess.FileExists(SavePath))
            {
                OS.ShellOpen(FileAccess.GetParentDir(SavePath));
            }
        };
        PreviewBtn.Pressed += () =>
        {
            PreviewContainer.ClearAndFreeChildren();
            if (CsvCache is null)
            {
                return;
            }

            TranslationServer.Clear();
            var langKeys = new HashSet<string>(CsvCache[0].Keys.Where(k => !CsvKeys.Equals(k)));
            var translateKeys = CsvCache.Select(dictionary => dictionary[CsvKeys]).ToList();

            Log.Info($"[翻译] 语言：{string.Join(",", langKeys)}");
            Log.Info($"[翻译] 字段：{string.Join(",", translateKeys)}");
            Dictionary<string, Godot.Collections.Dictionary> translateMap = new();

            var header = new HBoxContainer();
            PreviewContainer.AddChild(header);
            foreach (var langKey in langKeys)
            {
                translateMap.TryAdd(langKey, new());
                var btn = new CheckBox();
                btn.ButtonGroup = PreviewLangGroup;
                btn.Text = langKey;
                header.AddChild(btn);
                btn.Toggled += (toggled) =>
                {
                    if (toggled)
                    {
                        TranslationServer.SetLocale(langKey);
                    }
                };
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
        TranslateBtn.Pressed += async () =>
        {
            var sourceFilePath = SourceFile.Text;
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                Log.Error("[翻译] 没有设置源文件");
                return;
            }

            if (!File.Exists(sourceFilePath))
            {
                Log.Error("[翻译] 源文件不存在");
                return;
            }

            if (string.IsNullOrWhiteSpace(SourceLang))
            {
                Log.Error("[翻译] 没有设置源语言");
                return;
            }

            if (TargetLangs.Count <= 0)
            {
                Log.Error("[翻译] 没有设置目标语言");
                return;
            }

            if (!await OllamaApiClient.IsRunningAsync() | string.IsNullOrWhiteSpace(ModelName))
            {
                Log.Error("[ollama] 未启动或者未选择模型");
                return;
            }

            TranslateBtn.Disabled = true;
            TranslateProgress.Value = 0;
            TranslateProgress.MaxValue = TargetLangs.Count;
            if (sourceFilePath.EndsWith("csv"))
            {
                CsvCache = FileUtil.ReadCsv(sourceFilePath);
                var sourceDict = new Dictionary<string, string>();
                foreach (var row in CsvCache)
                {
                    if (!row.TryGetValue(CsvKeys, out var header))
                    {
                        Log.Error($"[翻译] 没有找到 keys 字段");
                        return;
                    }

                    if (row.TryGetValue(SourceLang, out var sourceText))
                    {
                        sourceDict[header] = sourceText;
                    }
                    else
                    {
                        Log.Error($"[翻译] 没有找到 {LocaleData.Language[SourceLang]} 相关翻译");
                        return;
                    }
                }

                foreach (var targetLang in TargetLangs)
                {
                    var resultDict = await TranslateChat(SourceLang, targetLang, sourceDict);
                    var idx = 0;
                    foreach (var (k, v) in resultDict)
                    {
                        CsvCache[idx][targetLang] = v;
                        idx++;
                    }

                    TranslateProgress.Value += 1;
                    Log.Info(
                        $"[翻译进度] ({TranslateProgress.Value}/{TranslateProgress.MaxValue}): 源语言 {SourceLang} key 数量：{CsvCache.Count}，目标语言 {targetLang} key 数量： {resultDict.Count}");
                }

                PreviewBtn.Disabled = false;
                SaveBtn.Disabled = false;
                TranslateBtn.Disabled = false;
                return;
            }

            if (sourceFilePath.EndsWith("translation"))
            {
                var translation = ResourceLoader.Load<Translation>(sourceFilePath);
                var messageList = translation.GetMessageList();
                var translatedMessageList = translation.GetTranslatedMessageList();
                var locale = translation.GetLocale();
                PreviewBtn.Disabled = false;
                SaveBtn.Disabled = false;
                TranslateBtn.Disabled = false;
                return;
            }
        };
    }

    public async Task<Dictionary<string, string>> TranslateChat(string sourceLang, string targetLang,
        Dictionary<string, string> sourceDict)
    {
        var prompt = string.Format(SystemPrompt, LocaleData.Language[sourceLang],
            LocaleData.Language[targetLang]);
        var result = "";
        var jsonContent = JsonUtil.ToJsonString(sourceDict);
        var chatRequest = new ChatRequest()
        {
            Model = ModelName,
            Messages =
            [
                new Message(ChatRole.System, prompt),
                new Message(ChatRole.User, jsonContent)
            ],
            Format = JsonElement.Parse(JsonSchema.FromSampleJson(jsonContent).ToJson())
        };
        try
        {
            await foreach (var chatResponseStream in OllamaApiClient.ChatAsync(chatRequest))
            {
                result += chatResponseStream.Message.Content;
            }

            return JsonUtil.ToDictionary<string, string>(result);
        }
        catch (Exception e)
        {
            Log.Info(result);
            Log.Error(e.Message);
            return null;
        }
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
                throw new Exception("[ollama] 未安装或者未启动成功");
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
                throw new Exception("[ollama] 未安装或者未启动成功");
            }
        }
        catch (Exception e)
        {
            Log.Error("[ollama] 未安装或者未启动成功");
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
                Log.Info(
                    $"[ollama] 模型 {modelDto.Name} ,running: {runningModelList.Any(m => m.Name.Equals(modelDto.Name))}");
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
            Log.Info($"[ollama ]加载模型 {ModelName}");
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
            Log.Info($"[ollama] 默认运行{runningModelName} - {valueTuples.Current.Index}");
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
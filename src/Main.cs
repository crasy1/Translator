using System.Collections.Generic;
using System.IO;
using Godot;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Translator.api;
using Translator.data;
using Translator.dto;

[SceneTree]
public partial class Main : CanvasLayer
{
    private string SourceLang { set; get; }
    private List<string> TargetLangs = [];
    private string ModelName { set; get; }

    private const string TemplatePrompt = @"你是一个专业的游戏本地化翻译

## 目的
你需要帮我翻译发给你的文本
翻译源语言为{0},目标语言为{1}
保持原文本的结构不变
只需要翻译每个 msgid 字段的值
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
        List<string> extensions = [".pot", ".po"];
        var extension = Path.GetExtension(filePath);
        if (!extensions.Contains(extension))
        {
            Log.Error($"文件格式错误 {extension}");
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

        Translate.Pressed += async () =>
        {
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

            if (!Ollama.IsRunning() | string.IsNullOrWhiteSpace(ModelName))
            {
                Log.Error("ollama未启动或者未选择模型");
                return;
            }

            var runningModelList = await Ollama.RunningModelList();
            if (!runningModelList.Any(m => m.Name == ModelName))
            {
                Log.Error($"选择的模型{ModelName}未启动");
                return;
            }

            var content = FileUtil.GetFileText(sourceFilePath);
            foreach (var targetLang in TargetLangs)
            {
                var prompt = string.Format(TemplatePrompt, LocaleData.Language[SourceLang],
                    LocaleData.Language[targetLang]);
                var chatParamDto = new ChatParamDto()
                {
                    Model = ModelName,
                    Messages =
                    [
                        MessageDto.System(prompt),
                        MessageDto.User(content)
                    ],
                    Stream = true
                };
                var result = "";
                await Ollama.StreamChat(chatParamDto, msg =>
                {
                    msg = msg.Replace("data:", "");
                    if (!msg.Contains("[DONE]"))
                    {
                        result += JObject.Parse(msg)["choices"][0]["delta"]["content"];
                    }
                });
                Log.Info(result);
                // var generate = await Ollama.Chat(chatParamDto);
                // Log.Info(generate);
            }
        };
    }

    /// <summary>
    /// 初始化ollama设置
    /// </summary>
    private async Task InitOllama()
    {
        Ollama.Start();
        if (!Ollama.IsRunning())
        {
            Log.Error("ollama未安装或者未启动成功");
            GetTree().Quit();
        }

        Log.Info(await Ollama.OllamaCmd("-v"));

        Host.FocusExited += () =>
        {
            Ollama.Host = string.IsNullOrWhiteSpace(Host.Text) ? Host.PlaceholderText : Host.Text;
        };
        ApiChat.FocusExited += () =>
        {
            Ollama.ApiChat = string.IsNullOrWhiteSpace(ApiChat.Text) ? ApiChat.PlaceholderText : ApiChat.Text;
        };
        ListModelBtn.Pressed += async () =>
        {
            var localModelList = await Ollama.LocalModelList();
            var runningModelList = await Ollama.RunningModelList();
            foreach (var modelDto in localModelList)
            {
                Log.Info($"{modelDto.Id} ,running: {runningModelList.Any(m => m.Name.Equals(modelDto.Id))}");
            }
        };
        Model.ItemSelected += async (index) =>
        {
            Model.SelfModulate = Colors.Red;
            ModelName = Model.GetItemText((int)index);
            var runningModelList = await Ollama.RunningModelList();
            if (runningModelList.Any(m => m.Name == ModelName))
            {
                Model.SelfModulate = Colors.Green;
                return;
            }

            foreach (var modelDto in runningModelList)
            {
                Ollama.UnloadModel(modelDto.Name);
            }

            if (await Ollama.LoadModel(ModelName))
            {
                Model.SelfModulate = Colors.Green;
            }
        };

        Model.Clear();
        var runningModelListTask = Ollama.RunningModelList();
        var localModelList = await Ollama.LocalModelList();
        foreach (var modelDto in localModelList)
        {
            Log.Info(modelDto.Id);
            Model.AddItem(modelDto.Id);
        }

        var runningModelList = await runningModelListTask;
        if (runningModelList.Count > 0)
        {
            var index = localModelList.FindIndex(i => i.Id.Equals(runningModelList[0].Name));
            Log.Info($"默认运行{runningModelList[0].Name} - {index}");
            Model.SelectAndEmitSignal(index);
        }
        else
        {
            if (localModelList.Count > 0)
            {
                Model.SelectAndEmitSignal(0);
            }
        }
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }
}
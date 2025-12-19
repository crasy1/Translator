using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Translator.dto;

namespace Translator.api;

public static class Ollama
{
    public static string Host { set; get; } = "http://localhost:11434";
    public static string ApiChat { set; get; } = "/v1/chat/completions";

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("ollama");
        return processes.Length > 0;
    }

    /// <summary>
    /// 检测并启动ollama
    /// </summary>
    /// <returns></returns>
    public static async Task<bool> Start()
    {
        if (IsRunning())
        {
            return true;
        }

        var version = await OllamaCmd("-v");
        if (string.IsNullOrWhiteSpace(version))
        {
            Log.Error("ollama 未安装");
            return false;
        }

        OllamaCmd("serve", false);
        return IsRunning();
    }

    public static async Task<string> OllamaCmd(string args, bool redirectStandardOutput = true)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = args,
                RedirectStandardOutput = redirectStandardOutput,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            using var reader = new System.IO.StreamReader(process.StandardOutput.BaseStream);
            var result = await reader.ReadToEndAsync();
            await process.WaitForExitAsync();
            return result.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 版本
    /// </summary>
    /// <returns></returns>
    public static async Task<string> Version()
    {
        try
        {
            var res = await HttpUtil.GetAsString($"{Host}/api/version");
            return JsonUtil.ToObj<Dictionary<string, string>>(res)?["version"];
        }
        catch (Exception e)
        {
            return null;
        }
    }

    /// <summary>
    /// 本地模型列表
    /// </summary>
    /// <returns></returns>
    public static async Task<List<OpenaiModelDto>> LocalModelList()
    {
        var res = await HttpUtil.GetAsString($"{Host}/v1/models");
        return JsonUtil.ToObj<OpenaiModelListDto>(res)?.Data;
    }

    /// <summary>
    /// 正在运行的模型列表
    /// </summary>
    /// <returns></returns>
    public static async Task<List<ModelDto>> RunningModelList()
    {
        var res = await HttpUtil.GetAsString($"{Host}/api/ps");
        return JsonUtil.ToObj<ModelListDto>(res)?.Models;
    }

    /// <summary>
    /// 生成
    /// </summary>
    /// <param name="model"></param>
    /// <param name="prompt"></param>
    /// <returns></returns>
    public static async Task<string> Generate(string model, string prompt)
    {
        var res = await HttpUtil.PostAsString($"{Host}/v1/completions",
            new { model = model, prompt = prompt }
        );
        return (string)JsonUtil.ToDictionary<string, object>(res)?["response"];
    }

    /// <summary>
    /// 聊天
    /// </summary>
    /// <param name="chatParamDto"></param>
    /// <returns></returns>
    public static async Task<string> Chat(ChatParamDto chatParamDto)
    {
        Log.Info(JsonUtil.ToJsonString(chatParamDto));
        var res = await HttpUtil.PostAsString($"{Host}{ApiChat}", chatParamDto);
        return JsonUtil.ToObj<ChatResultDto>(res).Message.Content;
    }

    /// <summary>
    /// 流式聊天
    /// </summary>
    /// <param name="chatParamDto"></param>
    /// <returns></returns>
    public static async Task StreamChat(ChatParamDto chatParamDto, Action<string> action)
    {
        Log.Info(JsonUtil.ToJsonString(chatParamDto));
        await HttpUtil.StreamPost($"{Host}{ApiChat}", chatParamDto, action);
    }

    /// <summary>
    /// 加载模型
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    public static async Task<bool> LoadModel(string model)
    {
        var res = await HttpUtil.PostAsString($"{Host}/api/generate", new { model = model });
        return (bool)JsonUtil.ToDictionary<string, object>(res)?["done"];
    }

    /// <summary>
    /// 卸载模型
    /// </summary>
    /// <param name="model"></param>
    /// <returns></returns>
    public static async Task<bool> UnloadModel(string model)
    {
        var res = await HttpUtil.PostAsString($"{Host}/api/generate", new { model = model, keep_alive = 0 });
        return (bool)JsonUtil.ToDictionary<string, object>(res)?["done"];
    }
}
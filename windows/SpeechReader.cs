using System.Runtime.InteropServices;

namespace NovelLens;

// 使用系统 SAPI，避免依赖 WebView2 对浏览器朗读 API 的支持程度。
internal sealed class SpeechReader : IDisposable
{
    private dynamic? speaker;
    private bool paused;
    private dynamic Speaker => speaker ??= Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("Windows 语音服务不可用"))!;

    public object Voices()
    {
        var result = new List<object>();
        dynamic tokens = Speaker.GetVoices();
        for (int i = 0; i < tokens.Count; i++)
        {
            dynamic token = tokens.Item(i);
            string languages = token.GetAttribute("Language");
            // 只列出日语，防止缺少日语包时使用中文或英语读日文。
            if (languages.Split(';').Any(code => code.Equals("411", StringComparison.OrdinalIgnoreCase)))
                result.Add(new { id = i.ToString(), name = (string)token.GetDescription() });
        }
        return result;
    }

    public object Execute(string action, string text, string voiceId, double rate)
    {
        switch (action)
        {
            case "voices": return Voices();
            case "speak":
                Stop();
                dynamic tokens = Speaker.GetVoices();
                if (!int.TryParse(voiceId, out int index) || index < 0 || index >= tokens.Count)
                    throw new InvalidOperationException("请选择日语语音；若列表为空，请在 Windows 设置中安装日语语音包");
                dynamic token = tokens.Item(index);
                if (!((string)token.GetAttribute("Language")).Split(';').Contains("411")) throw new InvalidOperationException("所选语音不是日语");
                if (text.Length > 10000) throw new InvalidOperationException("单段朗读文本过长");
                Speaker.Voice = token;
                Speaker.Rate = Math.Clamp((int)Math.Round((Math.Clamp(rate, .5, 2) - 1) * 6), -6, 6);
                Speaker.Speak(text, 19); // 异步 + 清空旧队列 + 明确作为纯文本处理。
                break;
            case "pause": if (!paused) { Speaker.Pause(); paused = true; } break;
            case "resume": if (paused) { Speaker.Resume(); paused = false; } break;
            case "stop": Stop(); break;
            case "status": break;
            default: throw new InvalidOperationException("未知朗读操作");
        }
        return new { speaking = speaker != null && (int)Speaker.Status.RunningState == 2, paused };
    }

    private void Stop()
    {
        if (speaker == null) return;
        if (paused) { Speaker.Resume(); paused = false; }
        Speaker.Speak("", 3);
    }
    public void Dispose()
    {
        Stop();
        if (speaker != null) { Marshal.FinalReleaseComObject(speaker); speaker = null; }
    }
}

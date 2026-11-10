using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NovelLens;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new NovelLensForm());
    }
}

internal sealed class NovelLensForm : Form
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly SpeechReader speech = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase) { "jisho.org", "translate.googleapis.com", "translate.google.com", "www.bing.com", "cn.bing.com", "api.mymemory.translated.net", "lingva.ml", "fanyi-api.baidu.com", "api-free.deepl.com", "api.deepl.com", "api.cognitive.microsofttranslator.com" };

    public NovelLensForm()
    {
        Text = "小说伴读 · 日中双语阅读";
        MinimumSize = new Size(760, 620); Size = new Size(1180, 860); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(web); Shown += async (_, _) => await StartAsync();
        FormClosed += (_, _) => speech.Dispose();
    }

    private async Task StartAsync()
    {
        try
        {
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovelLens");
            Directory.CreateDirectory(dataDir);
            await File.WriteAllTextAsync(Path.Combine(dataDir, "index.html"), ReadEmbeddedHtml(), new UTF8Encoding(false));
            // 将脚本、样式和字库从单文件 EXE 解包到同源目录，离线时也能显示日文。
            var assembly = typeof(Program).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Assets.")))
            {
                string destination = Path.Combine(dataDir, resource[7..]);
                // 注音词典保留子目录，保证本地分词器能按文件名找到所有词库。
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using Stream source = assembly.GetManifestResourceStream(resource)!;
                using Stream output = File.Create(destination);
                await source.CopyToAsync(output);
            }
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDir, "WebView2"));
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Settings.AreDevToolsEnabled = false; web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.novellens.local", dataDir, CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.WebMessageReceived += async (_, e) => await HandleRequestAsync(e);
            web.CoreWebView2.Navigate("https://app.novellens.local/index.html");
        }
        catch (Exception ex) { MessageBox.Show("应用启动失败。请确认已安装 Microsoft Edge WebView2 Runtime。\n\n" + ex.Message, "小说伴读", MessageBoxButtons.OK, MessageBoxIcon.Error); Close(); }
    }

    private async Task HandleRequestAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        string id = "";
        try
        {
            // 页面使用 postMessage(JSON.stringify(...))，先解开 JSON 字符串再读请求对象。
            using JsonDocument envelope = JsonDocument.Parse(e.WebMessageAsJson);
            using JsonDocument doc = JsonDocument.Parse(envelope.RootElement.ValueKind == JsonValueKind.String ? envelope.RootElement.GetString()! : e.WebMessageAsJson);
            JsonElement root = doc.RootElement;
            // 朗读消息走本地语音服务，不会发送小说到网络翻译站点。
            if (root.TryGetProperty("type", out var messageType) && messageType.GetString() == "speech")
            {
                id = root.GetProperty("id").GetString() ?? "";
                string action = root.GetProperty("action").GetString() ?? "status";
                string text = root.TryGetProperty("text", out var speechText) ? speechText.GetString() ?? "" : "";
                string voice = root.TryGetProperty("voice", out var speechVoice) ? speechVoice.GetString() ?? "" : "";
                double rate = root.TryGetProperty("rate", out var speechRate) ? speechRate.GetDouble() : 1;
                Reply(id, true, JsonSerializer.Serialize(speech.Execute(action, text, voice, rate)), "");
                return;
            }
            id = root.GetProperty("id").GetString() ?? ""; var url = new Uri(root.GetProperty("url").GetString() ?? throw new InvalidOperationException("缺少网址"));
            if (!AllowedHosts.Contains(url.Host)) throw new InvalidOperationException("不允许访问此地址");
            string method = root.TryGetProperty("method", out var methodNode) ? methodNode.GetString() ?? "GET" : "GET";
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            string contentType = "application/json";
            if (root.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
                foreach (var h in headers.EnumerateObject())
                    if (h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = h.Value.GetString() ?? contentType;
                    else request.Headers.TryAddWithoutValidation(h.Name, h.Value.GetString());
            // 保留表单服务的 Content-Type，避免将 DeepL 表单错误发送为 JSON。
            if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String) request.Content = new StringContent(body.GetString() ?? "", Encoding.UTF8, contentType);
            using HttpResponseMessage response = await Http.SendAsync(request); string bodyText = await response.Content.ReadAsStringAsync();
            Reply(id, response.IsSuccessStatusCode, bodyText, response.IsSuccessStatusCode ? "" : "HTTP " + (int)response.StatusCode);
        }
        catch (Exception ex) { Reply(id, false, "", ex.Message); }
    }

    private void Reply(string id, bool ok, string body, string error) => web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, body, error }));
    private static string ReadEmbeddedHtml()
    {
        Assembly asm = typeof(Program).Assembly; string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        using Stream stream = asm.GetManifestResourceStream(name)!; using StreamReader reader = new(stream); return reader.ReadToEnd();
    }
}

# 小说伴读 NovelLens

Windows 日语小说阅读助手：原文译文对照、圈词查词、生词本、复习、汉字注音、日语朗读及 TXT、EPUB、PDF 导入。

## 构建

需要 Windows、.NET 10 SDK 和 Microsoft Edge WebView2 Runtime：

```powershell
dotnet publish windows/NovelLens.csproj -c Release -r win-x64
```

首次还原需要访问 NuGet。EPUB 和 PDF 解析组件使用时联网加载；翻译和词典需要网络，免费服务可能限流或变化。日语朗读使用 Windows 日语语音包。

## 许可

本项目代码采用 MIT License。Noto Sans JP 字体采用 SIL Open Font License，见 `shared/OFL.txt`；kuromoji.js 使用 Apache License 2.0，其字典包含其他许可条款，见 `shared/kuromoji-LICENSE.txt` 和 `shared/kuromoji-NOTICE.md`。第三方组件分别遵循其自身许可。

本仓库目前发布 Windows 版源码；Android 版尚未同步到当前版本。

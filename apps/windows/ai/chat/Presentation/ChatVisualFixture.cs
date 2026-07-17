using Fowan.Ai.Shared.Models;

namespace Fowan.Ai.Chat.Windows.Presentation;

internal sealed record ChatVisualFixture(
    AiChannel Channel,
    AiCredential Credential,
    AiModelProfile Model,
    IReadOnlyList<AiConversationSummary> Conversations,
    IReadOnlyList<AiChatMessage> Messages)
{
    public static ChatVisualFixture Create() => new(
        new AiChannel("deepseek", "built-in", "DeepSeek", "https://api.deepseek.com", true, true),
        new AiCredential("fixture-credential", "deepseek", "DeepSeek · 工作密钥", "https://api.deepseek.com", "", true, "available", null, "2026-07-16T14:33:00+08:00", "2026-07-16T14:33:00+08:00"),
        new AiModelProfile("fixture-model", "fixture-credential", "deepseek-v4-pro", "deepseek-v4-pro", "fixture", true, "available", null, "2026-07-16T14:33:00+08:00", "2026-07-16T14:33:00+08:00"),
        [
            Conversation("theme", "WinUI 3 应用中实现主题切换", "2026-07-16T14:32:00+08:00"),
            Conversation("tasks", "推荐几款高效的任务管理工具", "2026-07-16T10:15:00+08:00"),
            Conversation("async", "C# 异步编程最佳实践", "2026-07-15T20:47:00+08:00"),
            Conversation("solid", "解释一下 SOLID 原则", "2026-07-15T16:22:00+08:00"),
            Conversation("sql", "如何优化 SQL 查询性能", "2025-05-25T09:31:00+08:00"),
            Conversation("dotnet", ".NET 9 有哪些新特性", "2025-05-23T18:05:00+08:00")
        ],
        [
            new AiChatMessage("user-1", "user", "如何在 WinUI 3 应用中优雅地实现主题切换？请给出示例代码。", "completed", null, 0, null, null, null, "2026-07-16T14:32:00+08:00"),
            new AiChatMessage("assistant-1", "assistant", "在 WinUI 3 应用中，可以通过资源字典和主题切换机制优雅地实现亮色/暗色主题切换。以下是实现思路：\n\n- 定义主题资源字典（Light、Dark）。\n- 使用 RequestedTheme 或自定义设置切换主题。\n- 通过合并资源字典动态切换。\n\n示例代码如下：\n\n```csharp\npublic void SetTheme(ElementTheme theme)\n{\n    var dict = theme == ElementTheme.Dark\n        ? new ResourceDictionary { Source = new Uri(\"ms-appx:///Themes/Dark.xaml\") }\n        : new ResourceDictionary { Source = new Uri(\"ms-appx:///Themes/Light.xaml\") };\n\n    Application.Current.Resources.MergedDictionaries.Clear();\n    Application.Current.Resources.MergedDictionaries.Add(dict);\n    AppWindow.TitleBar.PreferredTheme = theme;\n}\n```\n\n你可以根据需要扩展更多主题或跟随系统设置自动切换。", "completed", "user-1", 0, "DeepSeek", "工作密钥", "deepseek-v4-pro", "2026-07-16T14:33:00+08:00")
        ]);

    private static AiConversationSummary Conversation(string id, string title, string updatedAt) =>
        new(id, title, updatedAt, updatedAt);
}

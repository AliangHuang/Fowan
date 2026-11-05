using Fowan.Ai.Shared.Models;
using Microsoft.UI.Xaml.Controls;

namespace Fowan.Ai.Config.Windows.Presentation;

internal static class ConfigDialogForms
{
    internal sealed record ChannelForm(StackPanel Content, TextBox Name, TextBox Endpoint);

    internal sealed record CredentialForm(
        StackPanel Content,
        ComboBox Channel,
        TextBox Label,
        TextBox Endpoint,
        PasswordBox Secret);

    internal sealed record CredentialCreateForm(
        ScrollViewer Content,
        ComboBox Channel,
        TextBox Label,
        PasswordBox Secret,
        Expander Advanced,
        TextBox Endpoint,
        ListView ModelSelections,
        ToggleSwitch ThinkingEnabled);

    internal sealed record ModelForm(
        StackPanel Content,
        ComboBox Credential,
        TextBox ModelId,
        TextBox DisplayName,
        NumberBox ContextWindowTokens,
        NumberBox MaxOutputTokens,
        ComboBox? Preset);

    public static ChannelForm Channel(Func<string, string> text)
    {
        var name = new TextBox { Header = text("AI_ChannelName"), MinWidth = 420 };
        var endpoint = new TextBox { Header = text("AI_BaseUrl"), PlaceholderText = "https://example.com/v1" };
        return new(new StackPanel { Spacing = 12, Children = { name, endpoint } }, name, endpoint);
    }

    public static CredentialCreateForm CredentialCreate(
        Func<string, string> text,
        IEnumerable<AiChannel> channels,
        IEnumerable<AiPresetModel> presets)
    {
        var availableChannels = channels.ToList();
        var availablePresets = presets.ToList();
        var channel = new ComboBox
        {
            Header = $"{text("AI_Channel")}（必填）",
            ItemsSource = availableChannels,
            DisplayMemberPath = nameof(AiChannel.DisplayLabel),
            MinWidth = 420,
            SelectedItem = availableChannels.FirstOrDefault(item => item.Enabled)
        };
        var label = new TextBox { Header = $"{text("AI_CredentialName")}（必填）" };
        var secret = new PasswordBox { Header = $"{text("AI_ApiKey")}（必填）" };
        var endpoint = new TextBox
        {
            Header = $"{text("AI_BaseUrl")}（可选）",
            PlaceholderText = text("AI_BaseUrlDefault")
        };
        var models = new ListView
        {
            Header = "模型（可多选；留空使用默认模型）",
            SelectionMode = ListViewSelectionMode.Multiple,
            DisplayMemberPath = nameof(AiPresetModel.DisplayName),
            MinHeight = 96
        };
        var thinkingEnabled = new ToggleSwitch
        {
            Header = "DeepSeek 思考模式（可选）",
            OnContent = "开启",
            OffContent = "关闭",
            IsOn = true
        };
        void ApplyChannelOptions()
        {
            var selected = channel.SelectedItem as AiChannel;
            models.ItemsSource = selected is null
                ? []
                : availablePresets.Where(item => item.ChannelId == selected.Id).ToList();
            thinkingEnabled.Visibility = selected?.Id == "deepseek"
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        channel.SelectionChanged += (_, _) => ApplyChannelOptions();
        ApplyChannelOptions();

        var advancedContent = new StackPanel
        {
            Spacing = 12,
            Children = { endpoint, models, thinkingEnabled }
        };
        var advanced = new Expander
        {
            Header = "高级模式（可选）",
            Content = new ScrollViewer
            {
                MaxHeight = 280,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                Padding = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0),
                Content = advancedContent
            }
        };
        return new(
            new ScrollViewer
            {
                MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                Padding = new Microsoft.UI.Xaml.Thickness(0, 0, 8, 0),
                Content = new StackPanel { Spacing = 12, Children = { channel, label, secret, advanced } }
            },
            channel,
            label,
            secret,
            advanced,
            endpoint,
            models,
            thinkingEnabled);
    }

    public static CredentialForm Credential(
        Func<string, string> text,
        IEnumerable<AiChannel> channels,
        AiCredential? selected = null)
    {
        var channel = new ComboBox
        {
            Header = text("AI_Channel"),
            ItemsSource = channels.ToList(),
            DisplayMemberPath = nameof(AiChannel.DisplayLabel),
            MinWidth = 420
        };
        if (selected is not null)
        {
            channel.SelectedItem = channels.FirstOrDefault(item => item.Id == selected.ChannelId);
        }
        else
        {
            channel.SelectedItem = channels.FirstOrDefault(item => item.Enabled);
        }
        var label = new TextBox { Header = text("AI_CredentialName"), Text = selected?.Label ?? string.Empty };
        var endpoint = new TextBox
        {
            Header = text("AI_BaseUrl"),
            Text = selected?.BaseUrl ?? string.Empty,
            PlaceholderText = text("AI_BaseUrlDefault")
        };
        var secret = new PasswordBox { Header = text(selected is null ? "AI_ApiKey" : "AI_ReplaceApiKey") };
        return new(
            new StackPanel { Spacing = 12, Children = { channel, label, endpoint, secret } },
            channel,
            label,
            endpoint,
            secret);
    }

    public static ModelForm Model(
        Func<string, string> text,
        IEnumerable<AiCredential> credentials,
        IEnumerable<AiPresetModel> presets,
        AiModelProfile? selected = null)
    {
        var availableCredentials = credentials.Where(item => item.Enabled).ToList();
        var availablePresets = presets.ToList();
        var credential = new ComboBox
        {
            Header = text("AI_Credential"),
            ItemsSource = availableCredentials,
            DisplayMemberPath = nameof(AiCredential.DisplayLabel),
            MinWidth = 420,
            SelectedItem = selected is null
                ? null
                : availableCredentials.FirstOrDefault(item => item.Id == selected.CredentialId)
        };
        var modelId = new TextBox { Header = text("AI_ModelId"), Text = selected?.ModelId ?? string.Empty };
        var displayName = new TextBox { Header = text("AI_ModelName"), Text = selected?.DisplayName ?? string.Empty };
        var contextWindowTokens = new NumberBox { Header = "上下文窗口（Token）", Minimum = 2048, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = selected?.ContextWindowTokens ?? double.NaN };
        var maxOutputTokens = new NumberBox { Header = "最大输出（Token）", Minimum = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = selected?.MaxOutputTokens ?? double.NaN };

        var applyingDefaults = false;
        var contextWindowIsDefault = false;
        var maxOutputIsDefault = false;

        AiPresetModel? MatchedPreset() => credential.SelectedItem is AiCredential selectedCredential
            ? AiPresetModelDefaults.Find(availablePresets, selectedCredential.ChannelId, modelId.Text)
            : null;

        void ApplyModelDefaults(bool overwrite)
        {
            if (MatchedPreset() is not { } matched)
            {
                if (overwrite && (contextWindowIsDefault || maxOutputIsDefault))
                {
                    applyingDefaults = true;
                    if (contextWindowIsDefault) contextWindowTokens.Value = double.NaN;
                    if (maxOutputIsDefault) maxOutputTokens.Value = double.NaN;
                    applyingDefaults = false;
                    contextWindowIsDefault = false;
                    maxOutputIsDefault = false;
                }
                return;
            }

            var contextMissing = double.IsNaN(contextWindowTokens.Value);
            var outputMissing = double.IsNaN(maxOutputTokens.Value);
            if (!overwrite && !contextMissing && !outputMissing)
            {
                return;
            }

            applyingDefaults = true;
            if (overwrite || contextMissing)
            {
                contextWindowTokens.Value = matched.ContextWindowTokens;
                contextWindowIsDefault = true;
            }
            if (overwrite || outputMissing)
            {
                maxOutputTokens.Value = matched.MaxOutputTokens;
                maxOutputIsDefault = true;
            }
            applyingDefaults = false;
        }

        contextWindowTokens.ValueChanged += (_, _) =>
        {
            if (!applyingDefaults) contextWindowIsDefault = false;
        };
        maxOutputTokens.ValueChanged += (_, _) =>
        {
            if (!applyingDefaults) maxOutputIsDefault = false;
        };
        modelId.TextChanged += (_, _) => ApplyModelDefaults(overwrite: true);
        if (selected is not null)
        {
            credential.SelectionChanged += (_, _) => ApplyModelDefaults(overwrite: true);
            ApplyModelDefaults(overwrite: false);
            return new(
                new StackPanel { Spacing = 12, Children = { credential, modelId, displayName, contextWindowTokens, maxOutputTokens } },
                credential,
                modelId,
                displayName,
                contextWindowTokens,
                maxOutputTokens,
                null);
        }

        var preset = new ComboBox
        {
            Header = text("AI_PresetModel"),
            DisplayMemberPath = nameof(AiPresetModel.DisplayName),
            PlaceholderText = text("AI_PresetOptional")
        };
        credential.SelectionChanged += (_, _) =>
        {
            if (credential.SelectedItem is AiCredential selectedCredential)
            {
                preset.ItemsSource = availablePresets.Where(item => item.ChannelId == selectedCredential.ChannelId).ToList();
                ApplyModelDefaults(overwrite: true);
            }
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is AiPresetModel selectedPreset)
            {
                modelId.Text = selectedPreset.ModelId;
                displayName.Text = selectedPreset.DisplayName;
                contextWindowTokens.Value = selectedPreset.ContextWindowTokens;
                maxOutputTokens.Value = selectedPreset.MaxOutputTokens;
            }
        };
        return new(
            new StackPanel { Spacing = 12, Children = { credential, preset, modelId, displayName, contextWindowTokens, maxOutputTokens } },
            credential,
            modelId,
            displayName,
            contextWindowTokens,
            maxOutputTokens,
            preset);
    }
}

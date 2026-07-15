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

    internal sealed record ModelForm(
        StackPanel Content,
        ComboBox Credential,
        TextBox ModelId,
        TextBox DisplayName,
        ComboBox? Preset);

    public static ChannelForm Channel(Func<string, string> text)
    {
        var name = new TextBox { Header = text("AI_ChannelName"), MinWidth = 420 };
        var endpoint = new TextBox { Header = text("AI_BaseUrl"), PlaceholderText = "https://example.com/v1" };
        return new(new StackPanel { Spacing = 12, Children = { name, endpoint } }, name, endpoint);
    }

    public static CredentialForm Credential(
        Func<string, string> text,
        IEnumerable<AiChannel> channels,
        AiCredential? selected = null)
    {
        var channel = new ComboBox
        {
            Header = text("AI_Channel"),
            ItemsSource = channels.Where(item => item.Enabled).ToList(),
            DisplayMemberPath = nameof(AiChannel.DisplayName),
            MinWidth = 420
        };
        if (selected is not null)
        {
            channel.SelectedItem = channels.FirstOrDefault(item => item.Id == selected.ChannelId);
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
        if (selected is not null)
        {
            return new(
                new StackPanel { Spacing = 12, Children = { credential, modelId, displayName } },
                credential,
                modelId,
                displayName,
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
                preset.ItemsSource = presets.Where(item => item.ChannelId == selectedCredential.ChannelId).ToList();
            }
        };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is AiPresetModel selectedPreset)
            {
                modelId.Text = selectedPreset.ModelId;
                displayName.Text = selectedPreset.DisplayName;
            }
        };
        return new(
            new StackPanel { Spacing = 12, Children = { credential, preset, modelId, displayName } },
            credential,
            modelId,
            displayName,
            preset);
    }
}

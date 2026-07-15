using Fowan.Windows.Application;
using Fowan.Windows.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace Fowan.Windows.Presentation;

internal sealed class ToolboxProfileDialog(
    Func<ToolboxSnapshot> settings,
    Func<XamlRoot> xamlRoot,
    Func<string, string> localize,
    Func<string, Brush> themeBrush,
    Func<double, string?, Ellipse> avatarView,
    Func<Task<string?>> pickAvatar,
    Action<string, string> saveAndRebuild,
    Action<string, InfoBarSeverity> showInfo)
{
    public async Task ShowAsync()
    {
        var current = settings();
        var selectedAvatarPath = current.AvatarPath;
        var optionBorders = new List<Border>();
        var nameBox = new TextBox
        {
            Header = localize("Profile_Name"),
            Text = string.IsNullOrWhiteSpace(current.UserDisplayName) ? UserDefaults.DisplayName : current.UserDisplayName,
            PlaceholderText = localize("Profile_NamePlaceholder"), MinWidth = 320, MaxLength = 48
        };
        var nameError = new TextBlock
        {
            Text = localize("Profile_NameRequired"),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 196, 43, 28)),
            Visibility = Visibility.Collapsed
        };
        var avatarHost = new Border
        {
            Width = 84, Height = 84, CornerRadius = new CornerRadius(42), Child = avatarView(84, selectedAvatarPath)
        };
        Border? pickerPreview = null;
        void RefreshOptions()
        {
            var selected = UserDefaults.NormalizeAvatarPath(selectedAvatarPath);
            foreach (var option in optionBorders)
            {
                var active = string.Equals(selected, option.Tag?.ToString(), StringComparison.OrdinalIgnoreCase);
                option.BorderBrush = themeBrush(active ? "AccentStrokeColorDefaultBrush" : "CardStrokeColorDefaultBrush");
                option.BorderThickness = active ? new Thickness(3) : new Thickness(1);
            }
        }
        void SelectAvatar(string path)
        {
            selectedAvatarPath = path;
            avatarHost.Child = avatarView(84, path);
            if (pickerPreview is not null) pickerPreview.Child = avatarView(40, path);
            RefreshOptions();
        }
        var upload = new Button { Content = localize("Profile_UploadAvatar"), MinWidth = 120 };
        upload.Click += async (_, _) =>
        {
            var picked = await pickAvatar();
            if (!string.IsNullOrWhiteSpace(picked)) SelectAvatar(picked);
        };
        var random = new Button { Content = localize("Profile_RandomAvatar"), MinWidth = 120 };
        random.Click += (_, _) => SelectAvatar(UserDefaults.RandomAvatarPath());
        var avatarActions = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        avatarActions.Children.Add(new TextBlock
        {
            Text = localize("Profile_Avatar"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = themeBrush("TextFillColorPrimaryBrush")
        });
        avatarActions.Children.Add(upload);
        avatarActions.Children.Add(random);
        var avatarRow = new Grid { ColumnSpacing = 16 };
        avatarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        avatarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        avatarRow.Children.Add(avatarHost);
        Grid.SetColumn(avatarActions, 1);
        avatarRow.Children.Add(avatarActions);
        pickerPreview = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = avatarView(40, selectedAvatarPath)
        };
        var pickerContent = new Grid { ColumnSpacing = 10 };
        pickerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pickerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pickerContent.Children.Add(pickerPreview);
        var pickerText = new TextBlock
        {
            Text = localize("Profile_BuiltInAvatars"), VerticalAlignment = VerticalAlignment.Center,
            Foreground = themeBrush("TextFillColorPrimaryBrush")
        };
        Grid.SetColumn(pickerText, 1);
        pickerContent.Children.Add(pickerText);
        var chevron = new FontIcon
        {
            Glyph = "\uE70D", FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Foreground = themeBrush("TextFillColorSecondaryBrush")
        };
        Grid.SetColumn(chevron, 2);
        pickerContent.Children.Add(chevron);
        var pickerButton = new Button
        {
            MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 8, 10, 8),
            Content = pickerContent
        };
        AutomationProperties.SetName(pickerButton, localize("Profile_BuiltInAvatars"));
        ToolTipService.SetToolTip(pickerButton, localize("Profile_BuiltInAvatars"));
        var avatarGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8, Padding = new Thickness(4), MaxWidth = 420 };
        const int columns = 6;
        for (var column = 0; column < columns; column++) avatarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rows = (UserDefaults.BuiltInAvatarPaths.Length + columns - 1) / columns;
        for (var row = 0; row < rows; row++) avatarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var flyout = new Flyout
        {
            Content = new ScrollViewer
            {
                Width = 420, MaxHeight = 328, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = avatarGrid
            }
        };
        for (var index = 0; index < UserDefaults.BuiltInAvatarPaths.Length; index++)
        {
            var path = UserDefaults.BuiltInAvatarPaths[index];
            var option = new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent), BorderBrush = themeBrush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1), Tag = path, Child = avatarView(56, path)
            };
            optionBorders.Add(option);
            var button = new Button { Width = 62, Height = 62, Padding = new Thickness(0), Content = option };
            ToolTipService.SetToolTip(button, localize("Profile_BuiltInAvatars"));
            AutomationProperties.SetName(button, localize("Profile_BuiltInAvatars"));
            button.Click += (_, _) => { SelectAvatar(path); flyout.Hide(); };
            Grid.SetColumn(button, index % columns);
            Grid.SetRow(button, index / columns);
            avatarGrid.Children.Add(button);
        }
        RefreshOptions();
        pickerButton.Click += (_, _) => flyout.ShowAt(pickerButton);
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(avatarRow);
        stack.Children.Add(pickerButton);
        stack.Children.Add(nameBox);
        stack.Children.Add(nameError);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = localize("Profile_Title"), Content = stack,
            PrimaryButtonText = localize("Action_Save"), CloseButtonText = localize("Action_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        var shouldSave = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                nameError.Visibility = Visibility.Visible;
                nameBox.Focus(FocusState.Programmatic);
                args.Cancel = true;
                return;
            }
            shouldSave = true;
        };
        await dialog.ShowAsync();
        if (!shouldSave) return;
        saveAndRebuild(nameBox.Text, selectedAvatarPath);
        showInfo(localize("Profile_Saved"), InfoBarSeverity.Success);
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using GivreClone.App.Services;
using GivreClone.App.Services.Chat;

namespace GivreClone.App.Controls;

/// <summary>Texte fluide : les pastilles participent au retour à la ligne.</summary>
public sealed class ChatMessageText : TextBlock
{
    public static readonly StyledProperty<string> MessageProperty = AvaloniaProperty.Register<ChatMessageText, string>(nameof(Message), "");
    public string Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MessageProperty) RenderMessage();
    }

    private void RenderMessage()
    {
        Inlines?.Clear();
        foreach (var part in ChatMarkup.Parse(Message))
        {
            if (part.Kind is null) { Inlines!.Add(new Run(part.Text)); continue; }
            var launch = Stores.Launch;
            string name;
            IImage? icon = null;
            switch (part.Kind)
            {
                case "chatitem":
                    name = launch.ItemDisplayName(part.Id);
                    icon = Stores.InteractiveIcons.GetByIcon(launch.ItemIconId(part.Id));
                    break;
                case "chatmonster":
                    name = launch.GameNames.MonsterName(part.Id) ?? $"Monstre #{part.Id}";
                    icon = Stores.MonsterIcons.Get(part.Id);
                    break;
                case "chatachievement":
                    name = launch.GameData?.Achievement(part.Id)?.Name ?? $"Succès #{part.Id}";
                    icon = Stores.UiIcons.Get("achievement");
                    break;
                default:
                    name = part.Text;
                    icon = Stores.UiIcons.Get("group");
                    break;
            }
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
            if (icon is not null) row.Children.Add(new Image { Source = icon, Width = 18, Height = 18 });
            else row.Children.Add(new TextBlock { Text = part.Kind == "chatachievement" ? "★" : part.Kind == "chatmonster" ? "◆" : part.Kind == "lobby" ? "↗" : "▪", FontSize = 14 });
            row.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
            var chip = new Border { Child = row, CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 2), Margin = new Thickness(2, 0), BorderThickness = new Thickness(1) };
            chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Surface1Brush"));
            chip.Bind(Border.BorderBrushProperty, chip.GetResourceObservable("StrokeSoftBrush"));
            ToolTip.SetTip(chip, name);
            Inlines!.Add(new InlineUIContainer(chip));
        }
    }
}

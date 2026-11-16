using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace GivreClone.App.Behaviors;

/// <summary>Buttons whose content is a layout panel or an icon expose their control type ("Avalonia.Controls.StackPanel")
/// to screen readers and automation. Before each attach, an unnamed button is named from its tooltip, otherwise from
/// the texts it shows. An explicit AutomationProperties.Name always wins.</summary>
public static class AccessibleNameBehavior
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<Button>((button, _) => Apply(button));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TabItem>((tab, _) => Apply(tab));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ComboBox>((combo, _) => Apply(combo));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TextBox>((box, _) => Apply(box));
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<NumericUpDown>((num, _) => Apply(num));
    }

    private static void Apply(Button button)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))) return;
        string? name = Derive(ToolTip.GetTip(button) as string, button.Content as string,
            button.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
        if (name is not null) AutomationProperties.SetName(button, name);
    }

    private static void Apply(TabItem tab)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(tab))) return;
        string? name = Derive(ToolTip.GetTip(tab) as string, tab.Header as string,
            tab.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
        if (name is not null) AutomationProperties.SetName(tab, name);
    }

    private static void Apply(ComboBox combo)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(combo))) return;
        string? tip = ToolTip.GetTip(combo) as string;
        if (!string.IsNullOrWhiteSpace(tip))
        {
            AutomationProperties.SetName(combo, tip.Trim());
            return;
        }
        string? name = Derive(null, null,
            combo.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
        if (name is not null) AutomationProperties.SetName(combo, name);
    }

    private static void Apply(TextBox box)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(box))) return;
        string? watermark = box.Watermark;
        if (!string.IsNullOrWhiteSpace(watermark))
        {
            AutomationProperties.SetName(box, watermark.Trim());
            return;
        }
        string? tip = ToolTip.GetTip(box) as string;
        if (!string.IsNullOrWhiteSpace(tip)) AutomationProperties.SetName(box, tip.Trim());
    }

    private static void Apply(NumericUpDown num)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(num))) return;
        string? tip = ToolTip.GetTip(num) as string;
        if (!string.IsNullOrWhiteSpace(tip))
        {
            AutomationProperties.SetName(num, tip.Trim());
            return;
        }
        string? watermark = num.Watermark;
        if (!string.IsNullOrWhiteSpace(watermark)) AutomationProperties.SetName(num, watermark.Trim());
    }

    public static string? Derive(string? tooltip, string? textContent, IEnumerable<string?> visibleTexts)
    {
        if (!string.IsNullOrWhiteSpace(textContent)) return null;
        if (!string.IsNullOrWhiteSpace(tooltip)) return tooltip.Trim();
        var parts = visibleTexts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).Distinct().Take(3).ToArray();
        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }
}

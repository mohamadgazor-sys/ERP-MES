using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ERP.MES.Desktop.Views;

/// <summary>
/// A right-to-left yes/no question whose buttons read «بله» and «خیر» (the system MessageBox would show the operating system's language). «خیر» is the default and the Escape answer, so
/// Enter on a careless double-click never confirms an irreversible action.
/// </summary>
public static class ConfirmDialog
{
    public const string YesAutomationId = "ConfirmYesButton";
    public const string NoAutomationId = "ConfirmNoButton";

    public static bool Ask(Window? owner, string title, string message)
    {
        var answer = false;
        var window = new Window
        {
            Title = title,
            FlowDirection = FlowDirection.RightToLeft,
            SizeToContent = SizeToContent.Height,
            Width = 520,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        if (owner is { IsLoaded: true })
        {
            window.Owner = owner;
        }

        AutomationProperties.SetAutomationId(window, "ConfirmDialog");
        var yes = new Button { Content = "بله", MinWidth = 90, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 0, 0) };
        var no = new Button { Content = "خیر", MinWidth = 90, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, IsCancel = true };
        AutomationProperties.SetAutomationId(yes, YesAutomationId);
        AutomationProperties.SetAutomationId(no, NoAutomationId);
        yes.Click += (_, _) =>
        {
            answer = true;
            window.Close();
        };
        no.Click += (_, _) => window.Close();
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetAutomationId(text, "ConfirmMessage");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(text);
        panel.Children.Add(buttons);
        window.Content = panel;
        window.Loaded += (_, _) => no.Focus();
        window.ShowDialog();
        return answer;
    }
}

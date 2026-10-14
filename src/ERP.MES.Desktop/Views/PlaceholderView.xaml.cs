using System.Windows.Controls;

namespace ERP.MES.Desktop.Views;

/// <summary>Honest placeholder for a page that is not built yet.</summary>
public partial class PlaceholderView : UserControl
{
    public PlaceholderView(string title, string message)
    {
        InitializeComponent();
        HeadingText.Text = title;
        MessageText.Text = message;
    }
}

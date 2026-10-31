using System.Diagnostics;
using System.Windows;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop;

/// <summary>
/// The explicit "open drawing" action of the item lists (Decision Log #125 Q14). A drawing is read from the database and handed to the PDF viewer only when somebody clicks the button on a row;
/// nothing loads on its own. The button is the same in every list: it reads the item from the row (<see cref="IHasItemId"/>).
/// </summary>
public static class DrawingLauncher
{
    private static ItemDrawingService? _drawings;
    private static Func<Principal>? _currentUser;

    public static void Configure(ItemDrawingService drawings, Func<Principal> currentUser)
    {
        _drawings = drawings ?? throw new ArgumentNullException(nameof(drawings));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    public static string TempFolder => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ERP-MES-drawings");

    /// <summary>Opens the drawing of the item that the clicked row stands for; tells the user in Persian when there is none or it cannot be opened.</summary>
    public static async Task OpenAsync(object? sender)
    {
        if (_drawings is null || _currentUser is null || (sender as FrameworkElement)?.DataContext is not IHasItemId row || row.ItemId <= 0)
        {
            return;
        }

        try
        {
            var prepared = await DrawingOpening.PrepareAsync(_drawings, _currentUser(), row.ItemId, null, TempFolder);
            if (prepared.Path is null)
            {
                MessageBox.Show(prepared.Message, "نقشه آیتم", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(prepared.Path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show("باز کردن نقشه ممکن نشد؛ برنامه‌ای برای نمایش فایل PDF روی این رایانه نصب نیست یا فایل در دسترس نیست.", "نقشه آیتم", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

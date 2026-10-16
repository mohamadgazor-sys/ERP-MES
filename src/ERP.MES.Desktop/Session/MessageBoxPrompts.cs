using System.Windows;

namespace ERP.MES.Desktop.Session;

/// <summary>
/// The questions of the session lifecycle as Persian right-to-left message boxes. The texts are generic on purpose: they never say what the unsaved changes
/// contain, because the lock screen may be looked at by someone else (Decision Log #74).
/// </summary>
public sealed class MessageBoxPrompts : IUnsavedWorkPrompt, IDiscardConfirmation
{
    private const string Title = "سامانه ERP/MES";

    private const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    public Task<UnsavedDecision> AskAsync(bool saveOffered)
    {
        if (saveOffered)
        {
            var answer = MessageBox.Show(
                "تغییرات ذخیره‌نشده وجود دارد.\n«بله»: ذخیره و ادامه\n«خیر»: دور انداختن تغییرات و ادامه\n«انصراف»: ماندن در برنامه",
                Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel, Rtl);
            return Task.FromResult(answer switch
            {
                MessageBoxResult.Yes => UnsavedDecision.Save,
                MessageBoxResult.No => UnsavedDecision.Discard,
                _ => UnsavedDecision.Cancel,
            });
        }

        var discard = MessageBox.Show(
            "تغییرات ذخیره‌نشده وجود دارد و امکان ذخیره آن نیست.\n«تأیید»: دور انداختن تغییرات و ادامه\n«انصراف»: ماندن در برنامه",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel, Rtl);
        return Task.FromResult(discard == MessageBoxResult.OK ? UnsavedDecision.Discard : UnsavedDecision.Cancel);
    }

    public Task<bool> ConfirmDiscardAsync()
    {
        var answer = MessageBox.Show(
            "با ادامه، تغییرات ذخیره‌نشدهٔ کاربر قبلی از بین می‌رود. ادامه می‌دهید؟",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, Rtl);
        return Task.FromResult(answer == MessageBoxResult.Yes);
    }
}

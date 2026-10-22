using ERP.MES.Domain.Identity;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// The Persian texts of the customer and request screens. The Application reports codes (<see cref="OperationStatus"/>, <see cref="ValidationIssue"/>); this class owns
/// the wording. Nothing here decides anything.
/// </summary>
public static class Messages
{
    public const string NotAuthorized = "شما اجازهٔ انجام این کار را ندارید.";
    public const string NotFound = "رکورد پیدا نشد؛ ممکن است تغییر کرده باشد. فهرست دوباره بارگذاری شد.";
    public const string Conflict = "این رکورد را کاربر دیگری تغییر داده است. فهرست دوباره بارگذاری شد؛ کار خود را دوباره انجام دهید.";
    public const string Duplicate = "درخواستی با همین شماره برای این مشتری در همین سال شمسی از قبل وجود دارد.";
    public const string InvalidState = "در وضعیت فعلی درخواست، این کار مجاز نیست.";
    public const string Failed = "ذخیره یا خواندن اطلاعات ممکن نشد. دوباره تلاش کنید؛ اگر ادامه یافت با مدیر سیستم تماس بگیرید.";
    public const string Unexpected = "خطای غیرمنتظره رخ داد. دوباره تلاش کنید؛ اگر ادامه یافت با مدیر سیستم تماس بگیرید.";
    public const string NothingSelected = "ابتدا یک مورد را از فهرست انتخاب کنید.";

    public static string For<T>(Outcome<T> outcome) => outcome.Status switch
    {
        OperationStatus.NotAuthorized => NotAuthorized,
        OperationStatus.ValidationFailed => ForIssue(outcome.Issue, outcome.DateError),
        OperationStatus.NotFound => NotFound,
        OperationStatus.ConcurrencyConflict => Conflict,
        OperationStatus.DuplicateRequest => Duplicate,
        OperationStatus.InvalidState => InvalidState,
        OperationStatus.Failed => Failed,
        _ => string.Empty,
    };

    public static string ForIssue(ValidationIssue issue, JalaliDateError dateError) => issue switch
    {
        ValidationIssue.CustomerNameRequired => "نام مشتری را وارد کنید.",
        ValidationIssue.CustomerRequired => "مشتری را انتخاب کنید.",
        ValidationIssue.CustomerNotFound => "مشتری انتخاب‌شده دیگر وجود ندارد؛ مشتری دیگری را انتخاب کنید. درخواستی ثبت نشد.",
        ValidationIssue.CustomerInactive => "این مشتری غیرفعال است و برای او درخواست جدید ثبت نمی‌شود. درخواستی ثبت نشد؛ مشتری فعال دیگری را انتخاب کنید.",
        ValidationIssue.RequestNoRequired => "شمارهٔ درخواست را وارد کنید.",
        ValidationIssue.RequestNoWhitespace => "شمارهٔ درخواست نباید با فاصله یا نویسهٔ خالی شروع یا تمام شود.",
        ValidationIssue.RequestDateRequired => "تاریخ درخواست را وارد کنید.",
        ValidationIssue.RequestDateInvalid => ForDateError(dateError),
        ValidationIssue.ItemNoRequired => "شمارهٔ آیتم را وارد کنید.",
        ValidationIssue.ItemNoDuplicate => "آیتمی با همین شماره در این درخواست وجود دارد.",
        ValidationIssue.PartNameRequired => "نام قطعه را وارد کنید.",
        ValidationIssue.QuantityInvalid => "تعداد باید عددی بزرگ‌تر از صفر باشد.",
        ValidationIssue.WorkTypeInvalid => "نوع کار (ساخت یا تعمیر) را انتخاب کنید.",
        ValidationIssue.PriorityInvalid => "اولویت معتبر نیست.",
        ValidationIssue.ReasonRequired => "دلیل یا توضیح را وارد کنید.",
        ValidationIssue.ReasonTooLong => "دلیل یا توضیح بیش از ۲۰۰۰ نویسه است.",
        ValidationIssue.DrawingOutcomeInvalid => "نتیجهٔ نقشه‌کشی را انتخاب کنید.",
        ValidationIssue.RequestPdfRequired => "پیش از ارسال به مدیریت، فایل PDF درخواست را پیوست کنید.",
        ValidationIssue.PdfEmpty => "فایل انتخاب‌شده خالی است.",
        ValidationIssue.PdfTooLarge => "حجم فایل بیشتر از حد مجاز است (پیش‌فرض ۲۰ مگابایت).",
        ValidationIssue.PdfNotPdf => "فایل انتخاب‌شده یک PDF معتبر نیست.",
        ValidationIssue.PdfFileNameInvalid => "نام فایل باید با .pdf پایان یابد.",
        ValidationIssue.RequestClosedForItems => "به درخواست لغوشده یا بسته‌شده آیتم جدید اضافه نمی‌شود.",
        ValidationIssue.StageActionInvalid => "این کار مرحله‌ای پشتیبانی نمی‌شود.",
        ValidationIssue.VersionsMissing => "اطلاعات این ردیف کامل نیست؛ فهرست را دوباره بارگذاری کنید.",
        _ => Failed,
    };

    public static string ForDateError(JalaliDateError error) => error switch
    {
        JalaliDateError.Empty => "تاریخ درخواست را وارد کنید.",
        JalaliDateError.InvalidCharacter => "تاریخ فقط می‌تواند شامل رقم و «/» باشد (مثال: ۱۴۰۵/۰۷/۲۰).",
        JalaliDateError.InvalidFormat => "قالب تاریخ باید سال/ماه/روز باشد (مثال: ۱۴۰۵/۰۷/۲۰).",
        JalaliDateError.YearOutOfRange => "سال تاریخ معتبر نیست.",
        JalaliDateError.MonthOutOfRange => "ماه تاریخ باید بین ۱ و ۱۲ باشد.",
        JalaliDateError.DayOutOfRange => "روز تاریخ برای این ماه معتبر نیست.",
        _ => "تاریخ معتبر نیست (مثال: ۱۴۰۵/۰۷/۲۰).",
    };

    public static string ActionText(RequestAction action) => action switch
    {
        RequestAction.Submit => "ارسال به مدیریت",
        RequestAction.Approve => "تأیید مدیریت",
        RequestAction.Reject => "رد مدیریت",
        RequestAction.Reopen => "بازگشایی برای تصمیم جدید مدیریت",
        RequestAction.ForwardToDrawing => "ارسال به نقشه‌کشی",
        RequestAction.RecordDrawingResult => "نتیجهٔ نقشه‌کشی",
        RequestAction.SendBackToDrawing => "بازگشت به نقشه‌کشی",
        _ => string.Empty,
    };

    public static string OutcomeText(DrawingOutcome outcome) => outcome switch
    {
        DrawingOutcome.FullyAccepted => "کاملاً پذیرفته شد",
        DrawingOutcome.PartiallyAccepted => "بخشی پذیرفته شد",
        DrawingOutcome.Rejected => "رد شد",
        DrawingOutcome.MoreInformationNeeded => "نیاز به اطلاعات بیشتر",
        DrawingOutcome.ReturnedForCorrection => "بازگشت برای اصلاح",
        _ => string.Empty,
    };

    public static string DepartmentText(string code) => code switch
    {
        DepartmentCodes.Central => "مرکزی",
        DepartmentCodes.Management => "مدیریت",
        DepartmentCodes.Planning => "برنامه‌ریزی",
        DepartmentCodes.Drawing => "نقشه‌کشی",
        DepartmentCodes.Warehouse => "انبار",
        DepartmentCodes.Production => "تولید",
        DepartmentCodes.Qc => "کنترل کیفیت",
        DepartmentCodes.MaintenancePm => "نگهداری و تعمیرات",
        DepartmentCodes.System => "سیستم",
        _ => code,
    };

    public static string WorkTypeText(WorkType type) => type == WorkType.Make ? "ساخت" : "تعمیر";

    public static string StatusText(RequestStatus status) => status switch
    {
        RequestStatus.Created => "ایجادشده",
        RequestStatus.SentToManagement => "ارسال‌شده به مدیریت",
        RequestStatus.MgmtApprovedAtPlanning => "تأییدشده توسط مدیریت",
        RequestStatus.MgmtRejectedAtPlanning => "ردشده توسط مدیریت",
        RequestStatus.UnderDrawingReview => "در بررسی نقشه‌کشی",
        RequestStatus.DrawingReturnedToPlanning => "بازگشته از نقشه‌کشی به برنامه‌ریزی",
        RequestStatus.Cancelled => "لغوشده",
        RequestStatus.Closed => "بسته‌شده",
        _ => string.Empty,
    };
}

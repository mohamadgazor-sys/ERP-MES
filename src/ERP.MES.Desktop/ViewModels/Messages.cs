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
    /// <summary>
    /// Keeps free-form Latin text (request, item and contract numbers, machine codes, file names) readable inside a right-to-left Persian sentence: DEMO-1405-014 must not read 014-1405-DEMO. WPF does not honour the newer
    /// bidi isolates and, as screenshots showed, not even explicit embeddings (U+202A...U+202C) inside DataGrid cells, so every neutral character (hyphen, slash, dot, space) is put between two left-to-right marks
    /// (U+200E), which are strong characters that every implementation orders the same way. The text itself is unchanged.
    /// </summary>
    public static string Ltr(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        const char Lrm = '\u200E';
        var builder = new System.Text.StringBuilder(text.Length * 3 + 2);
        builder.Append(Lrm);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(Lrm).Append(c).Append(Lrm);
            }
        }

        return builder.Append(Lrm).ToString();
    }

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
        ValidationIssue.ItemNoDuplicate => "آیتمی با همین شمارهٔ مشتری در این درخواست وجود دارد؛ شمارهٔ مشتری باید یکتا باشد.",
        ValidationIssue.CustomerItemRefInvalid => "شمارهٔ آیتم مشتری حداکثر ۴۰ نویسه است و نباید در ابتدا یا انتها فاصله داشته باشد.",
        ValidationIssue.ItemParentInvalid => "والد انتخاب‌شده در این درخواست وجود ندارد.",
        ValidationIssue.ItemParentNotAssembly => "والد انتخاب‌شده مونتاژ نیست؛ ابتدا آن را مونتاژ کنید.",
        ValidationIssue.ItemHierarchyCycle => "آیتم را نمی‌توان زیر خودش یا زیر یکی از زیرمجموعه‌هایش برد.",
        ValidationIssue.ItemHierarchyTooDeep => "تودرتویی مونتاژها نباید از چهار سطح بیشتر شود.",
        ValidationIssue.ItemHierarchyLocked => "برای آیتم‌های این شاخه (یا والدشان) مرحله تعریف شده است؛ رابطهٔ والد و فرزند دیگر تغییر نمی‌کند.",
        ValidationIssue.ItemHasChildren => "مونتاژی که زیرمجموعه دارد را نمی‌توان به آیتم عادی برگرداند.",
        ValidationIssue.ItemSequenceExhausted => "همهٔ شماره‌های ۱ تا ۹۹۹ این سطح استفاده شده است.",
        ValidationIssue.AssemblyChildrenNotComplete => "مرحلهٔ اول مونتاژ تا پایان مسیر و تأیید کنترل کیفیت همهٔ زیرمجموعه‌ها آزاد نمی‌شود.",
        ValidationIssue.PartNameRequired => "نام قطعه را وارد کنید.",
        ValidationIssue.QuantityInvalid => "تعداد باید عددی بزرگ‌تر از صفر باشد.",
        ValidationIssue.WorkTypeInvalid => "نوع کار (ساخت یا تعمیر) را انتخاب کنید.",
        ValidationIssue.PriorityInvalid => "اولویت معتبر نیست.",
        ValidationIssue.ReasonRequired => "دلیل یا توضیح را وارد کنید.",
        ValidationIssue.ReasonTooLong => "دلیل یا توضیح بیش از ۲۰۰۰ نویسه است.",
        ValidationIssue.DrawingOutcomeInvalid => "نتیجهٔ نقشه‌کشی را انتخاب کنید.",
        ValidationIssue.CodeInvalid => "کد باید ۱ تا ۲۰ نویسه از A تا Z، ارقام، «_» یا «-» باشد.",
        ValidationIssue.CodeDuplicate => "این کد قبلاً ثبت شده است.",
        ValidationIssue.NameRequired => "نام فارسی را وارد کنید.",
        ValidationIssue.UnitKindInvalid => "نوع واحد را انتخاب کنید.",
        ValidationIssue.UnitNotFound => "واحد انتخاب‌شده وجود ندارد.",
        ValidationIssue.ConversionSameUnit => "تبدیل به دو واحد متفاوت نیاز دارد.",
        ValidationIssue.FactorInvalid => "ضریب تبدیل باید عددی بزرگ‌تر از صفر باشد.",
        ValidationIssue.ConversionDuplicate => "بین این دو واحد قبلاً تبدیل تعریف شده است (تبدیل معکوس خودکار محاسبه می‌شود).",
        ValidationIssue.BaseUnitInvalid => "واحد شمارش پایه باید یک واحد فعال از نوع «شمارشی» باشد.",
        ValidationIssue.BaseUnitsInvalid => "تعداد واحد پایه باید حداقل ۱ باشد.",
        ValidationIssue.SetMembersInvalid => "ست باید حداقل دو عضو داشته باشد و هر عضو شرح و تعداد حداقل ۱ بخواهد.",
        ValidationIssue.ProductionUnitInvalid => "واحد تولیدی انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.MachineInvalid => "ماشین انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.RateInvalid => "نرخ ساعتی باید عددی بزرگ‌تر از صفر (ریال در ساعت) باشد؛ بدون جداکنندهٔ هزارگان.",
        ValidationIssue.EffectiveDateRequired => "تاریخ شروع اعمال نرخ را وارد کنید.",
        ValidationIssue.EffectiveDateInvalid => "تاریخ شروع اعمال نرخ معتبر نیست (مثال: ۱۴۰۵/۰۷/۲۰).",
        ValidationIssue.RateNotLater => "تاریخ شروع نرخ جدید باید بعد از آخرین نرخ ثبت‌شدهٔ این ماشین باشد؛ تاریخچه بازنویسی نمی‌شود.",
        ValidationIssue.ImportFileMissing => "فایل اکسل را انتخاب کنید.",
        ValidationIssue.ImportFileTooLarge => "حجم فایل بیش از ۲۰ مگابایت است.",
        ValidationIssue.ImportFileChanged => "فایل با فایلِ پیش‌نمایش‌شده فرق دارد؛ دوباره پیش‌نمایش بگیرید.",
        ValidationIssue.ImportHasErrors => "فایل خطا دارد و ثبت نمی‌شود؛ خطاها را در فایل اصلاح و دوباره پیش‌نمایش بگیرید. هیچ تغییری ذخیره نشد.",
        ValidationIssue.ImportDecisionMissing => "برای هر ردیفِ دارای تعارض باید «رد کردن» یا «به‌روزرسانی» را انتخاب کنید. هیچ تغییری ذخیره نشد.",
        ValidationIssue.ImportDecisionInvalid => "آیتمی که مسیر مرحله یا تأیید کنترل کیفیت دارد با ورود از اکسل به‌روز نمی‌شود؛ فقط «رد کردن» ممکن است.",
        ValidationIssue.ItemGateNotSatisfied => "هنوز دروازه‌ها برقرار نیست؛ تعریف آیتم پس از تأیید مدیریت، نتیجهٔ پذیرفتهٔ نقشه‌کشی، تصمیم قرارداد (برای ساخت)، پاسخ استعلام‌ها و بسته شدن پرسش‌ها ممکن است.",
        ValidationIssue.ItemSpecificationIncomplete => "برای آیتم ساخت، نوع تعداد، ماده و وزن (کیلوگرم برای هر واحد پایه) الزامی است.",
        ValidationIssue.WeightInvalid => "وزن باید بزرگ‌تر از صفر، حداکثر ۹۹٬۹۹۹٬۹۹۹٫۹۹۹۹ کیلوگرم و با حداکثر ۴ رقم اعشار باشد.",
        ValidationIssue.RepairFieldsMissing => "برای آیتم تعمیر، تاریخ تحویل و کد کالای انبار الزامی است.",
        ValidationIssue.WarehouseCodeInvalid => "کد کالای انبار باید ۱ تا ۴۰ نویسه و بدون فاصلهٔ ابتدا یا انتها باشد.",
        ValidationIssue.DeliveryBeforeRequest => "تاریخ تحویل آیتم نمی‌تواند پیش از تاریخ درخواست باشد.",
        ValidationIssue.DimensionInvalid => "طول و قطر باید بزرگ‌تر از صفر (میلی‌متر)، حداکثر ۹۹٬۹۹۹٬۹۹۹٫۹۹ و با حداکثر ۲ رقم اعشار باشد.",
        ValidationIssue.QuantityTypeInvalid => "نوع تعداد انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.ItemNotFound => "آیتم انتخاب‌شده در این درخواست وجود ندارد.",
        ValidationIssue.StageTitleRequired => "عنوان مرحله را وارد کنید (حداکثر ۲۰۰ نویسه).",
        ValidationIssue.StageMinutesInvalid => "دقیقهٔ برنامه‌ریزی‌شده برای هر قطعه باید بزرگ‌تر از صفر باشد.",
        ValidationIssue.MachineUnitMismatch => "ماشین انتخاب‌شده به واحد تولیدی انتخاب‌شده تعلق ندارد.",
        ValidationIssue.ClarificationTextInvalid => "متن را وارد کنید (حداکثر ۲۰۰۰ نویسه).",
        ValidationIssue.ClarificationRequestState => "پرسش توضیحی فقط هنگام بررسی نقشه‌کشی ثبت می‌شود.",
        ValidationIssue.DispositionInvalid => "برای «ثبت به‌عنوان شرط» و «بازگشت بدون تغییر» توضیح لازم است؛ برای «اصلاح در دور جدید» باید دور بعدیِ همین درخواست (لغو‌نشده) انتخاب شود.",
        ValidationIssue.SupplyRouteInvalid => "مسیر تأمین (توضیح) را وارد کنید.",
        ValidationIssue.InquiryRequestState => "استعلام مواد اولیه فقط هنگام بررسی نقشه‌کشی یا پس از بازگشت از نقشه‌کشی ممکن است.",
        ValidationIssue.InquiryLineInvalid => "جنس و شکل ماده الزامی است (حداکثر ۱۰۰ نویسه)؛ ابعاد در صورت ورود باید بزرگ‌تر از صفر باشد.",
        ValidationIssue.InquiryNoLines => "پیش از ارسال، دست‌کم یک ردیف ماده اضافه کنید.",
        ValidationIssue.InquiryAnswerInvalid => "پاسخ باید «بله» یا «خیر» باشد.",
        ValidationIssue.ContractNotApplicable => "برای درخواست تعمیر مرحلهٔ قرارداد وجود ندارد.",
        ValidationIssue.ContractRequestNotReturned => "درخواست باید پس از تأیید مدیریت و بررسی نقشه‌کشی در وضعیت «بازگشته از نقشه‌کشی به برنامه‌ریزی» باشد.",
        ValidationIssue.ContractDrawingNotAccepted => "نتیجهٔ نقشه‌کشی باید «پذیرفته‌شده» یا «پذیرفته‌شده بخشی» باشد.",
        ValidationIssue.ContractNoRequired => "برای ارسال، شمارهٔ قرارداد را وارد کنید.",
        ValidationIssue.ContractDateRequired => "برای ارسال، تاریخ قرارداد را وارد کنید.",
        ValidationIssue.ContractPdfRequired => "برای ارسال، فایل PDF قرارداد را پیوست کنید.",
        ValidationIssue.ContractTextTooLong => "شمارهٔ قرارداد (حداکثر ۵۰) یا عنوان (حداکثر ۲۰۰) بیش از حد مجاز است.",
        ValidationIssue.ItemContractRequestUndecided => "ابتدا باید لزوم قرارداد برای کل درخواست ثبت شود.",
        ValidationIssue.ItemContractRequestNotRequired => "برای این درخواست قرارداد لازم نیست؛ ابتدا تصمیم درخواست را (با ذکر دلیل) تغییر دهید.",
        ValidationIssue.ItemContractInProduction => "مرحله‌ای از این آیتم آزاد یا شروع شده است؛ تصمیم قرارداد آیتم دیگر قابل تغییر نیست.",
        ValidationIssue.SessionStageNotOperational => "این مرحله آزاد و در انتظار یا در حال انجام نیست؛ نوبت کاری برای آن ثبت نمی‌شود.",
        ValidationIssue.SessionMachineNotInUnit => "ماشین انتخاب‌شده متعلق به واحد تولید این مرحله نیست.",
        ValidationIssue.SessionMachineUnusable => "ماشین انتخاب‌شده فعال نیست.",
        ValidationIssue.SessionOperatorUnavailable => "اپراتور در دسترس نیست (غیرفعال، غایب در این روز یا بدون مجوز این واحد).",
        ValidationIssue.SessionOutsideShiftReasonRequired => "اپراتور در این زمان در شیفت یا اضافه‌کاری تأییدشده نیست؛ دلیل کار خارج از شیفت را بنویسید.",
        ValidationIssue.SessionMachineBusy => "روی این ماشین نوبت کاری دیگری در حال اجراست.",
        ValidationIssue.SessionOperatorBusy => "این اپراتور نوبت کاری دیگری در حال اجرا دارد.",
        ValidationIssue.SessionNotSupervisor => "شما سرپرست این واحد تولید نیستید.",
        ValidationIssue.SessionNotOwn => "فقط نوبت‌های کاری خودتان را می‌توانید انجام دهید.",
        ValidationIssue.SessionTimeBasisConflict => "زمان این مرحله با گزارش کار روزانه (یا با نوبت کاری) ثبت می‌شود و مبنای دیگر روی آن مجاز نیست.",
        ValidationIssue.SessionPauseReasonInvalid => "دلیل توقف معتبر و فعال نیست.",
        ValidationIssue.SessionPauseNoteRequired => "برای این دلیل توقف، توضیح لازم است.",
        ValidationIssue.SessionHandoverSameOperator => "تحویل به اپراتور دیگری لازم است.",
        ValidationIssue.SessionCorrectionInvalid => "اصلاح زمان معتبر نیست (ترتیب زمان‌ها، آینده یا دلیل).",
        ValidationIssue.SessionCorrectionAfterSubmission => "مرحله به کنترل کیفیت ارسال شده است؛ ابتدا برنامه‌ریزی ارسال را برگرداند.",
        ValidationIssue.SessionApproverIsActor => "تأییدکنندهٔ زمان نباید همان کسی باشد که نوبت را ثبت یا شروع کرده است.",
        ValidationIssue.SubmitBlockedBySessions => "تا زمانی که نوبت کاری تخصیص‌یافته، در حال اجرا یا متوقف وجود دارد، مرحله به کنترل کیفیت ارسال نمی‌شود؛ آن‌ها را پایان دهید یا لغو کنید.",
        ValidationIssue.SubmitNoFinishedSession => "این مرحله با نوبت کاری ثبت می‌شود اما هنوز نوبت پایان‌یافته‌ای ندارد.",
        ValidationIssue.CostingSessionsNotApproved => "زمان واقعی برخی نوبت‌های کاری پایان‌یافتهٔ این آیتم هنوز توسط برنامه‌ریزی تأیید نشده است؛ ابتدا آن‌ها را تأیید کنید.",
        ValidationIssue.SessionNothingToPause => "نوبت کاری فعالی برای توقف وجود ندارد.",
        ValidationIssue.PauseReasonCodeInvalid => "کد دلیل توقف نامعتبر یا تکراری است.",
        ValidationIssue.ItemContractUnchanged => "آیتم از قبل همین تصمیم را دارد.",
        ValidationIssue.ContractAlreadyDecided => "تصمیم دربارهٔ لزوم قرارداد قبلاً ثبت شده است؛ برای تغییر از «تغییر تصمیم» با ذکر دلیل استفاده کنید.",
        ValidationIssue.MaterialInvalid => "ماده انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.PriceInvalid => "قیمت باید عددی بزرگ‌تر از صفر (ریال بر کیلوگرم) باشد؛ بدون جداکنندهٔ هزارگان.",
        ValidationIssue.PriceNotLater => "تاریخ شروع قیمت جدید باید بعد از آخرین قیمت ثبت‌شدهٔ این ماده باشد؛ تاریخچه بازنویسی نمی‌شود.",
        ValidationIssue.MaterialTextTooLong => "جنس یا شکل ماده بیش از ۱۰۰ نویسه است.",
        ValidationIssue.OperatorNameInvalid => "نام کامل اپراتور را وارد کنید (حداکثر ۲۰۰ نویسه).",
        ValidationIssue.UserInvalid => "حساب کاربری انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.UserAlreadyLinked => "این حساب کاربری به اپراتور دیگری وصل است.",
        ValidationIssue.OperatorInvalid => "اپراتور انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.AssignmentExists => "برای این اپراتور و ماشین تخصیص باز وجود دارد؛ ابتدا آن را ببندید.",
        ValidationIssue.AssignmentClosed => "این تخصیص قبلاً بسته شده است.",
        ValidationIssue.EndBeforeStart => "آخرین روز نمی‌تواند پیش از روز شروع باشد.",
        ValidationIssue.QualificationExists => "برای این اپراتور و ماشین صلاحیت باز ثبت شده است؛ ابتدا آن را پایان دهید.",
        ValidationIssue.QualificationEnded => "این صلاحیت قبلاً پایان یافته است.",
        ValidationIssue.DateRequired => "تاریخ را وارد کنید.",
        ValidationIssue.DateInvalid => "تاریخ معتبر نیست (مثال: ۱۴۰۵/۰۷/۲۰).",
        ValidationIssue.RequestPdfRequired => "پیش از ارسال به مدیریت، فایل PDF درخواست را پیوست کنید.",
        ValidationIssue.PdfEmpty => "فایل انتخاب‌شده خالی است.",
        ValidationIssue.PdfTooLarge => "حجم فایل بیشتر از حد مجاز است (پیش‌فرض ۲۰ مگابایت).",
        ValidationIssue.PdfNotPdf => "فایل انتخاب‌شده یک PDF معتبر نیست.",
        ValidationIssue.PdfFileNameInvalid => "نام فایل باید با .pdf پایان یابد.",
        ValidationIssue.RequestClosedForItems => "به درخواست لغوشده یا بسته‌شده آیتم جدید اضافه نمی‌شود.",
        ValidationIssue.StageActionInvalid => "این کار مرحله‌ای پشتیبانی نمی‌شود.",
        ValidationIssue.VersionsMissing => "اطلاعات این ردیف کامل نیست؛ فهرست را دوباره بارگذاری کنید.",
        ValidationIssue.QueueUnitInvalid => "واحد تولید انتخاب‌شده وجود ندارد یا فعال نیست.",
        ValidationIssue.QueueRowsInvalid => "ردیف‌های انتخاب‌شده برای این کار معتبر نیستند (باید مرحلهٔ آزادشدهٔ همین واحد باشند، بدون تکرار؛ برای لغو یا تأیید، باید جایگزینی فعال داشته باشند). کاری انجام نشد.",
        ValidationIssue.QueueReasonRequired => "دلیل را وارد کنید (حداکثر ۵۰۰ نویسه).",
        ValidationIssue.QueueUnitNotPermitted => "شما به صف این واحد دسترسی ندارید.",
        ValidationIssue.StageGateNotSatisfied => "شرط‌های آزادسازی تولید برقرار نیست (تأیید مدیریت، نقشه‌کشی، قرارداد، استعلام یا پرسش‌ها) یا درخواست لغو/بسته شده است؛ مرحله شروع یا آزاد نشد.",
        ValidationIssue.StageStopped => "این مرحله متوقف شده است و تا ازسرگیری ادامه نمی‌یابد.",
        ValidationIssue.StageUnitNotDesignated => "شما به واحد تولید این مرحله منصوب نشده‌اید؛ برنامه‌ریزی باید شما را به این واحد منصوب کند.",
        ValidationIssue.JobReportOperatorNotLinked => "حساب شما به هیچ اپراتور فعالی وصل نیست؛ برنامه‌ریزی باید حساب را به رکورد اپراتور شما وصل کند.",
        ValidationIssue.JobReportDateInvalid => "تاریخ گزارش معتبر نیست (نباید در آینده باشد) یا واحد این شیفت را اجرا نمی‌کند.",
        ValidationIssue.JobReportDuplicate => "برای این اپراتور، تاریخ و شیفت قبلاً گزارش ثبت شده است؛ همان گزارش را ویرایش کنید.",
        ValidationIssue.JobReportLineInvalid => "یکی از خط‌های گزارش معتبر نیست (ماشین واحد دیگر، مرحلهٔ آزادنشده یا مرحلهٔ واحد دیگر، زمان نامعتبر، توقف بدون دلیل یا تعداد منفی).",
        ValidationIssue.JobReportLinesOverlap => "ساعت‌های گزارش با هم یا با گزارش ثبت‌شدهٔ دیگر همین اپراتور هم‌پوشانی دارند.",
        ValidationIssue.JobReportReasonRequired => "دلیل بازگرداندن را وارد کنید (حداکثر ۳۰۰ نویسه).",
        ValidationIssue.JobReportReviewerNotAllowed => "فقط سرپرست همین واحد (و در نبود سرپرست، برنامه‌ریزی) گزارش را بررسی می‌کند و کسی گزارش خودش را بررسی نمی‌کند.",
        ValidationIssue.InspectionEntriesInvalid => "تصمیم‌های بازرسی معتبر نیستند (حداقل یک تصمیم، تعداد مثبت، و برای بازکاری و ضایعات دلیل الزامی است).",
        ValidationIssue.InspectionExceedsAwaiting => "تعداد تصمیم‌ها از واحدهای در انتظار کنترل کیفیت بیشتر است؛ هیچ واحدی دوبار شمرده نمی‌شود.",
        ValidationIssue.StageNotCountable => "تعداد این مرحله عدد صحیحی از واحدهای پایه نیست؛ بازرسی تعدادی ممکن نیست.",
        ValidationIssue.StageUnitRequired => "این مرحله در هیچ واحد تولیدی برنامه‌ریزی نشده است؛ برنامه‌ریزی باید ابتدا مرحله را به یک واحد تولید اختصاص دهد (جریان نمایشی بدون واحد بازنشسته شده است).",
        ValidationIssue.ShortfallNoteRequired => "یادداشت یا شمارهٔ ارجاع تصمیم کسری را وارد کنید (حداکثر ۳۰۰ نویسه).",
        ValidationIssue.ShiftCodeDuplicate => "این کد شیفت قبلاً استفاده شده است.",
        ValidationIssue.ShiftTimesInvalid => "ساعت‌های شیفت معتبر نیست (شروع و پایان نباید یکسان باشند؛ استراحت باید داخل شیفت باشد؛ قالب ساعت: ۰۸:۰۰).",
        ValidationIssue.UnitShiftExists => "این واحد همین شیفت را از قبل دارد.",
        ValidationIssue.UnitShiftOverlapNotMarked => "این شیفت با شیفت دیگر همین واحد هم‌پوشانی دارد؛ برنامه‌ریزی باید هم‌پوشانی را صریحاً علامت بزند (ظرفیت جدا).",
        ValidationIssue.CapacityInvalid => "ظرفیت معتبر نیست (حداقل ۰، حداکثر ۱ تا ۱۰۰۰ و حداقل نباید از حداکثر بیشتر باشد).",
        ValidationIssue.CalendarExceptionExists => "برای همین تاریخ و محدوده، همین استثنا از قبل فعال است.",
        ValidationIssue.CalendarReasonRequired => "دلیل را وارد کنید (حداکثر ۳۰۰ نویسه؛ برای روز کاری اضافه، مرجع تأیید سرپرست را بنویسید).",
        ValidationIssue.ScheduleUnitShiftNotConfigured => "این واحد این شیفت را اجرا نمی‌کند (یا شیفت غیرفعال است).",
        ValidationIssue.ScheduleDayOff => "در این تاریخ، شیفت این واحد کاری نیست (الگوی هفتگی، تعطیلی یا استثنا).",
        ValidationIssue.ScheduleOperatorAbsent => "اپراتور در این تاریخ مرخصی/غیبت ثبت‌شده دارد.",
        ValidationIssue.ScheduleWorkerOverlap => "اپراتور در همان روز شیفت هم‌پوشان دارد؛ یک نفر در دو شیفت هم‌پوشان برنامه‌ریزی نمی‌شود.",
        ValidationIssue.ScheduleCapacityExceeded => "ظرفیت حداکثر این شیفت واحد در آن روز پر است.",
        ValidationIssue.ScheduleMachineBusy => "ماشین در همین زمان برای اپراتور دیگری رزرو است؛ فقط هم‌پوشانی تحویل صریح (با مدت و یادداشت) مجاز است.",
        ValidationIssue.HandoverInvalid => "هم‌پوشانی تحویل معتبر نیست (ماشین، یادداشت و مدت ۱ تا ۴۸۰ دقیقه لازم است و مدت باید هم‌پوشانی واقعی را پوشش دهد).",
        ValidationIssue.ReceiptNotReady => "آیتم هنوز قابل دریافت نیست: همهٔ مرحله‌ها باید با بازرسی تعدادی تأیید شده و واحد پذیرفته‌شده داشته باشند.",
        ValidationIssue.ReceiptDuplicate => "این آیتم قبلاً دریافت نهایی شده است؛ دریافت تکراری ثبت نمی‌شود.",
        ValidationIssue.ReceiptRequired => "ابتدا دریافت نهایی آیتم را ثبت کنید.",
        ValidationIssue.FinishedSentNeedsPrice => "تکمیل/ارسال فقط پس از تأیید قیمت آخرین محاسبهٔ هزینه توسط مدیریت مجاز است.",
        ValidationIssue.FinishedSentDuplicate => "این آیتم قبلاً تکمیل/ارسال‌شده ثبت شده است.",
        ValidationIssue.CompletionInputInvalid => "تاریخ (شمسی و نه در آینده)، شمارهٔ سند (۱ تا ۱۰۰ نویسه) یا یادداشت (حداکثر ۳۰۰ نویسه) معتبر نیست.",
        ValidationIssue.CostingNotReady => "هزینه هنوز قابل محاسبه نیست: همهٔ مرحله‌های آیتم باید با بازرسی تعدادی تأیید شده، واحد پذیرفته‌شده داشته باشند و تاریخ آزادسازی مرحلهٔ اول ثبت شده باشد.",
        ValidationIssue.CostingNoApprovedTime => "برای این آیتم هنوز ساعت تأییدشده‌ای در گزارش کار نیست؛ هزینهٔ معتبر بدون آن محاسبه نمی‌شود (جایگزینی با زمان برنامه‌ای انجام نمی‌شود).",
        ValidationIssue.CostingRateMissing => "برای ماشین یا تاریخی نرخ ساعتی تعریف نشده است؛ هزینه محاسبه نشد.",
        ValidationIssue.CostingPriceMissing => "برای مادهٔ اولیه در تاریخ آزادسازی قیمت تعریف نشده است؛ هزینه محاسبه نشد.",
        ValidationIssue.ItemPriceInvalid => "قیمت معتبر نیست (عدد صحیح مثبت ریال؛ برای تغییر قیمت محاسبه‌شده دلیل لازم است؛ برای آیتم قراردادی مبلغ و شمارهٔ قرارداد الزامی است).",
        ValidationIssue.OvertimeNotSupervisor => "فقط سرپرست تعیین‌شدهٔ همین واحد اضافه‌کاری پیشنهاد می‌دهد و کسی برای خودش پیشنهاد نمی‌دهد.",
        ValidationIssue.OvertimeLimitsNotConfigured => "برنامه‌ریزی هنوز سقف اضافه‌کاری را تعریف نکرده است؛ تا آن زمان اضافه‌کاری تأیید نمی‌شود.",
        ValidationIssue.OvertimeLimitsInvalid => "سقف معتبر نیست (روزانه ۱ تا ۱۴۴۰ دقیقه؛ هفتگی از سقف روزانه کمتر و از ۱۰۰۸۰ دقیقه بیشتر نباشد).",
        ValidationIssue.OvertimeInvalid => "اضافه‌کاری معتبر نیست (اپراتور، واحد، ماشین، تاریخ و ساعت‌ها؛ حداکثر ۱۲ ساعت؛ دلیل الزامی و حداکثر ۳۰۰ نویسه است).",
        ValidationIssue.OvertimeSelfDecision => "درخواست اضافه‌کاری را نمی‌توان توسط خود پیشنهاددهنده تأیید یا رد کرد.",
        ValidationIssue.OvertimeOperatorAbsent => "اپراتور در این روز غایب (مرخصی یا بیماری) ثبت شده است.",
        ValidationIssue.OvertimeOperatorOverlap => "اضافه‌کاری باید بیرون از شیفت عادی باشد و با شیفت برنامه‌ریزی‌شده یا اضافه‌کاری دیگر همین اپراتور هم‌پوشانی ندارد.",
        ValidationIssue.OvertimeMachineBusy => "ماشین در این زمان برای اپراتور دیگری (شیفت یا اضافه‌کاری) رزرو است.",
        ValidationIssue.DesignationUserInvalid => "کاربر انتخاب‌شده وجود ندارد، فعال نیست یا کاربر تولید نیست.",
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

    public static string InquiryStatusText(ERP.MES.Domain.Inquiries.InquiryStatus status) => status switch
    {
        ERP.MES.Domain.Inquiries.InquiryStatus.Draft => "پیش‌نویس",
        ERP.MES.Domain.Inquiries.InquiryStatus.Sent => "ارسال‌شده به انبار",
        ERP.MES.Domain.Inquiries.InquiryStatus.PartiallyAnswered => "بخشی پاسخ داده شده",
        ERP.MES.Domain.Inquiries.InquiryStatus.Answered => "پاسخ داده شده",
        ERP.MES.Domain.Inquiries.InquiryStatus.Closed => "بسته‌شده",
        _ => "لغوشده",
    };

    public static string InquiryActionText(ERP.MES.Domain.Inquiries.InquiryAction action) => action switch
    {
        ERP.MES.Domain.Inquiries.InquiryAction.Created => "ایجاد",
        ERP.MES.Domain.Inquiries.InquiryAction.LineAdded => "افزودن ردیف",
        ERP.MES.Domain.Inquiries.InquiryAction.LineRemoved => "حذف ردیف از پیش‌نویس",
        ERP.MES.Domain.Inquiries.InquiryAction.Sent => "ارسال به انبار",
        ERP.MES.Domain.Inquiries.InquiryAction.Answered => "پاسخ انبار",
        ERP.MES.Domain.Inquiries.InquiryAction.AnswerChanged => "تغییر پاسخ انبار",
        ERP.MES.Domain.Inquiries.InquiryAction.Closed => "بستن پس از بررسی",
        ERP.MES.Domain.Inquiries.InquiryAction.DispositionRecorded => "تعیین تکلیف پاسخ «خیر» (نقشه‌کشی)",
        ERP.MES.Domain.Inquiries.InquiryAction.SupplyRouteRecorded => "ثبت مسیر تأمین (برنامه‌ریزی)",
        _ => "لغو",
    };

    public static string ClarificationStatusText(ERP.MES.Domain.Inquiries.ClarificationStatus status) => status switch
    {
        ERP.MES.Domain.Inquiries.ClarificationStatus.Open => "باز؛ منتظر پاسخ برنامه‌ریزی",
        ERP.MES.Domain.Inquiries.ClarificationStatus.Answered => "پاسخ داده شده؛ منتظر بستن توسط نقشه‌کشی",
        _ => "بسته‌شده",
    };

    public static string DispositionKindText(ERP.MES.Domain.Inquiries.DispositionKind kind) => kind switch
    {
        ERP.MES.Domain.Inquiries.DispositionKind.RevisedInNewRound => "اصلاح در دور جدید",
        ERP.MES.Domain.Inquiries.DispositionKind.RecordedAsCondition => "ثبت به‌عنوان شرط نتیجهٔ فنی",
        _ => "بازگشت نتیجه بدون تغییر",
    };

    public static string GateBlockerText(ERP.MES.Application.Requests.GateBlocker blocker) => blocker switch
    {
        ERP.MES.Application.Requests.GateBlocker.DefinitionGate => "دروازهٔ مدیریت/نقشه‌کشی/قرارداد برقرار نیست",
        ERP.MES.Application.Requests.GateBlocker.InquiryLinesUnanswered => "ردیف‌های بی‌پاسخ استعلام مواد اولیه",
        ERP.MES.Application.Requests.GateBlocker.NoAnswerWithoutDisposition => "پاسخ «خیر» بدون تعیین تکلیف نقشه‌کشی",
        _ => "پرسش توضیحی نقشه‌کشی بسته نشده است",
    };

    public static string ImportRowStatusText(ERP.MES.Application.Imports.ImportRowStatus status) => status switch
    {
        ERP.MES.Application.Imports.ImportRowStatus.New => "جدید",
        ERP.MES.Application.Imports.ImportRowStatus.Unchanged => "بدون تغییر",
        ERP.MES.Application.Imports.ImportRowStatus.Conflict => "تعارض؛ انتخاب لازم است",
        ERP.MES.Application.Imports.ImportRowStatus.Locked => "تعارض؛ قفل (فقط رد کردن)",
        _ => "خطا",
    };

    public static string ImportFieldText(ERP.MES.Application.Imports.ImportField field) => field switch
    {
        ERP.MES.Application.Imports.ImportField.DeliveryDate => "تاریخ تحویل آیتم",
        ERP.MES.Application.Imports.ImportField.WarehouseItemCode => "کد کالای انبار",
        ERP.MES.Application.Imports.ImportField.PartName => "نام قطعه",
        ERP.MES.Application.Imports.ImportField.Quantity => "تعداد",
        ERP.MES.Application.Imports.ImportField.Priority => "اولویت",
        ERP.MES.Application.Imports.ImportField.QuantityType => "نوع تعداد",
        ERP.MES.Application.Imports.ImportField.Material => "ماده",
        ERP.MES.Application.Imports.ImportField.Weight => "وزن",
        ERP.MES.Application.Imports.ImportField.Length => "طول",
        _ => "قطر",
    };

    public static string ImportMessageText(ERP.MES.Application.Imports.ImportMessage message)
    {
        var detail = message.Detail is null ? string.Empty : " (" + message.Detail + ")";
        return message.Issue switch
        {
            ERP.MES.Application.Imports.ImportIssue.FileNotXlsx => "فایل، اکسل با پسوند xlsx نیست (فایل xls قدیمی یا CSV پذیرفته نمی‌شود).",
            ERP.MES.Application.Imports.ImportIssue.FileTooLarge => "فایل یا محتوای بازشدهٔ آن بیش از حد مجاز است.",
            ERP.MES.Application.Imports.ImportIssue.FileCorrupt => "فایل آسیب‌دیده است یا ساختار آن خوانده نمی‌شود.",
            ERP.MES.Application.Imports.ImportIssue.NoWorksheet => "فایل برگهٔ کاری ندارد.",
            ERP.MES.Application.Imports.ImportIssue.TooManyRows => "تعداد ردیف‌ها از حد مجاز (۵۰۰۰) بیشتر است.",
            ERP.MES.Application.Imports.ImportIssue.HeaderRowMissing => "ردیف عنوان ستون‌ها پیدا نشد.",
            ERP.MES.Application.Imports.ImportIssue.HeaderMissing => "ستون الزامی پیدا نشد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.HeaderDuplicate => "عنوان ستون تکراری است" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.HeaderUnknown => "ستون ناشناخته نادیده گرفته شد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.MoreThanOneWorksheet => "فایل بیش از یک برگه دارد؛ فقط برگهٔ اول خوانده شد.",
            ERP.MES.Application.Imports.ImportIssue.NoDataRows => "فایل ردیف داده ندارد.",
            ERP.MES.Application.Imports.ImportIssue.ItemNoMissing => "شمارهٔ آیتم خالی است.",
            ERP.MES.Application.Imports.ImportIssue.ItemNoNumeric => "شمارهٔ آیتم باید سلول متنی باشد (سلول عددی «1.10» را به 1.1 تبدیل می‌کند)" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.ItemNoDuplicate => "شمارهٔ آیتم مشتری در فایل تکراری است؛ اولین بار در ردیف " + message.Detail + ". آیتم موجود با حدس تطبیق داده نمی‌شود.",
            ERP.MES.Application.Imports.ImportIssue.CustomerRefTooLong => "شمارهٔ آیتم مشتری از ۴۰ نویسه بیشتر است" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.NoCustomerRef => "این ردیف شمارهٔ آیتم مشتری ندارد؛ همیشه آیتم جدید ساخته می‌شود و با آیتم موجود تطبیق داده نمی‌شود (واردکردن دوبارهٔ همین فایل آن را دوباره اضافه می‌کند).",
            ERP.MES.Application.Imports.ImportIssue.AssemblyFlagInvalid => "ستون «مونتاژ» فقط «بله» یا خالی می‌پذیرد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.ParentRefUnknown => "والد با این مرجع نه در درخواست است و نه در ردیف‌های پیشین فایل (والد باید قبل از فرزند بیاید)" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.ParentNotAssembly => "والد مونتاژ نیست؛ در ردیف خودش «بله» بگذارید یا پیش از ورود در درخواست مونتاژش کنید" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.ParentTooDeep => "تودرتویی مونتاژها نباید از چهار سطح بیشتر شود" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.ParentLocked => "برای والد مرحله تعریف شده و دیگر زیرمجموعه نمی‌گیرد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.ParentIsSelf => "ردیف خودش را والد خود قرار داده است" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.PartNameMissing => "نام قطعه خالی است.",
            ERP.MES.Application.Imports.ImportIssue.QuantityInvalid => "تعداد باید عددی بزرگ‌تر از صفر باشد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.WorkTypeUnknown => "نوع کار باید «ساخت» یا «تعمیر» باشد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.WorkTypeMismatch => "نوع کار ردیف با نوع کار درخواست یکی نیست؛ درخواست نوع واحد دارد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.QuantityTypeMissing => "کد نوع تعداد برای آیتم ساخت الزامی است.",
            ERP.MES.Application.Imports.ImportIssue.QuantityTypeUnknown => "کد نوع تعداد وجود ندارد یا فعال نیست" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.MaterialMissing => "کد ماده برای آیتم ساخت الزامی است.",
            ERP.MES.Application.Imports.ImportIssue.MaterialUnknown => "کد ماده وجود ندارد یا فعال نیست" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.WeightMissing => "وزن برای آیتم ساخت الزامی است.",
            ERP.MES.Application.Imports.ImportIssue.WeightInvalid => "وزن نامعتبر است (بزرگ‌تر از صفر، حداکثر ۴ رقم اعشار)" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.RepairFieldsMissing => "برای آیتم تعمیر، تاریخ تحویل و کد کالای انبار الزامی است.",
            ERP.MES.Application.Imports.ImportIssue.DeliveryDateInvalid => "تاریخ تحویل آیتم شمسی معتبر نیست" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.DeliveryBeforeRequest => "تاریخ تحویل آیتم پیش از تاریخ درخواست است" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.WarehouseCodeInvalid => "کد کالای انبار باید حداکثر ۴۰ نویسه و بدون فاصلهٔ ابتدا یا انتها باشد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.DimensionInvalid => "طول یا قطر نامعتبر است (بزرگ‌تر از صفر، حداکثر ۲ رقم اعشار)" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.PriorityUnknown => "اولویت باید «عادی»، «فوری» یا «خیلی فوری» باشد" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.StageIncomplete => "مرحلهٔ " + message.Detail + ": عنوان، دقیقه و کد واحد هر سه لازم است.",
            ERP.MES.Application.Imports.ImportIssue.StageHeaderBeyondLimit => "ستون «" + message.Detail + "» از حداکثر مجاز مراحل (" + ERP.MES.Application.Imports.ItemImportTemplate.MaxStages + " مرحله) بیشتر است؛ فایل را اصلاح کنید.",
            ERP.MES.Application.Imports.ImportIssue.StageGap => "مرحلهٔ " + message.Detail + " پر شده ولی مرحلهٔ قبل از آن خالی است.",
            ERP.MES.Application.Imports.ImportIssue.StageMinutesInvalid => "مرحلهٔ " + message.Detail + ": دقیقه برای هر قطعه نامعتبر است.",
            ERP.MES.Application.Imports.ImportIssue.StageUnitUnknown => "کد واحد تولیدی وجود ندارد یا فعال نیست" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.StageMachineUnknown => "کد ماشین وجود ندارد یا فعال نیست" + detail + ".",
            ERP.MES.Application.Imports.ImportIssue.StageMachineUnitMismatch => "ماشین به واحد انتخاب‌شده تعلق ندارد" + detail + ".",
            _ => "آیتم از قبل وجود دارد؛ ستون‌های مرحله فقط برای آیتم جدید اعمال می‌شود و مرحله‌های موجود حفظ می‌شود.",
        };
    }

    public static string ContractStatusText(ERP.MES.Domain.Contracts.ContractStatus status) => status switch
    {
        ERP.MES.Domain.Contracts.ContractStatus.NotRequired => "قرارداد لازم نیست",
        ERP.MES.Domain.Contracts.ContractStatus.Draft => "پیش‌نویس (نزد برنامه‌ریزی)",
        ERP.MES.Domain.Contracts.ContractStatus.PendingManagement => "در انتظار مدیریت",
        ERP.MES.Domain.Contracts.ContractStatus.Approved => "تأیید شده",
        _ => "ردشده؛ بازگشته به برنامه‌ریزی",
    };

    public static string ContractVersionStateText(ERP.MES.Domain.Contracts.ContractVersionState state) => state switch
    {
        ERP.MES.Domain.Contracts.ContractVersionState.Draft => "پیش‌نویس",
        ERP.MES.Domain.Contracts.ContractVersionState.Submitted => "ارسال‌شده",
        _ => "کنار گذاشته‌شده",
    };

    public static string ContractActionText(ERP.MES.Domain.Contracts.ContractAction action) => action switch
    {
        ERP.MES.Domain.Contracts.ContractAction.DecidedNotRequired => "ثبت: قرارداد لازم نیست",
        ERP.MES.Domain.Contracts.ContractAction.DecidedRequired => "ثبت: قرارداد لازم است",
        ERP.MES.Domain.Contracts.ContractAction.RequirementChanged => "تغییر تصمیم",
        ERP.MES.Domain.Contracts.ContractAction.DraftSaved => "ذخیرهٔ پیش‌نویس",
        ERP.MES.Domain.Contracts.ContractAction.PdfAttached => "پیوست PDF",
        ERP.MES.Domain.Contracts.ContractAction.Submitted => "ارسال به مدیریت",
        ERP.MES.Domain.Contracts.ContractAction.Approved => "تأیید مدیریت",
        ERP.MES.Domain.Contracts.ContractAction.Rejected => "رد مدیریت",
        _ => "شروع نسخهٔ جدید",
    };

    public static string DefinitionBlockText(ERP.MES.Domain.Contracts.DefinitionBlock block) => block switch
    {
        ERP.MES.Domain.Contracts.DefinitionBlock.None => "—",
        ERP.MES.Domain.Contracts.DefinitionBlock.RequestNotReturnedFromDrawing => "درخواست هنوز پس از تأیید مدیریت از نقشه‌کشی به برنامه‌ریزی بازنگشته است",
        ERP.MES.Domain.Contracts.DefinitionBlock.DrawingResultNotAccepted => "نتیجهٔ نقشه‌کشی پذیرفته نشده است",
        ERP.MES.Domain.Contracts.DefinitionBlock.ContractDecisionMissing => "برنامه‌ریزی هنوز دربارهٔ لزوم قرارداد تصمیم نگرفته است",
        ERP.MES.Domain.Contracts.DefinitionBlock.ContractInPreparation => "قرارداد در حال تهیه است",
        ERP.MES.Domain.Contracts.DefinitionBlock.ContractPendingManagement => "قرارداد منتظر تصمیم مدیریت است",
        _ => "قرارداد رد شده و باید اصلاح و دوباره ارسال شود",
    };

    public static string DepartmentText(string code) => code switch
    {
        DepartmentCodes.Central => "منشی",
        DepartmentCodes.Management => "مدیریت",
        DepartmentCodes.Planning => "برنامه‌ریزی",
        DepartmentCodes.Drawing => "نقشه‌کشی",
        DepartmentCodes.Warehouse => "انبار",
        DepartmentCodes.Production => "تولید",
        DepartmentCodes.Qc => "کنترل کیفیت",
        DepartmentCodes.MaintenancePm => "نگهداری و تعمیرات",
        DepartmentCodes.System => "سیستم",
        DepartmentCodes.ProductionManager => "مدیر تولید",
        _ => code,
    };

    public static string UnitKindText(ERP.MES.Domain.Quality.UnitKind kind) => kind switch
    {
        ERP.MES.Domain.Quality.UnitKind.Count => "شمارشی",
        ERP.MES.Domain.Quality.UnitKind.Mass => "جرم",
        ERP.MES.Domain.Quality.UnitKind.Length => "طول",
        _ => "سایر",
    };

    public static string QueueCategoryText(ERP.MES.Application.Production.QueueCategory? category) => category switch
    {
        ERP.MES.Application.Production.QueueCategory.InProgress => "در حال اجرا",
        ERP.MES.Application.Production.QueueCategory.Stopped => "متوقف / مشکل‌دار",
        ERP.MES.Application.Production.QueueCategory.Waiting => "در انتظار",
        _ => "—",
    };

    public static string QueueBlockText(ERP.MES.Application.Production.QueueBlock block) => block switch
    {
        ERP.MES.Application.Production.QueueBlock.None => "قابل اجرا",
        ERP.MES.Application.Production.QueueBlock.StageStopped => "متوقف شده",
        ERP.MES.Application.Production.QueueBlock.RequestClosed => "درخواست لغو یا بسته شده است",
        ERP.MES.Application.Production.QueueBlock.GateNotSatisfied => "شرط‌های آزادسازی تولید برقرار نیست",
        ERP.MES.Application.Production.QueueBlock.RepairIncomplete => "آیتم تعمیر ناقص است: تاریخ تحویل یا کد کالای انبار ثبت نشده",
        ERP.MES.Application.Production.QueueBlock.WaitingChildren => "منتظر پایان مسیر و تأیید کنترل کیفیت همهٔ زیرمجموعه‌ها",
        ERP.MES.Application.Production.QueueBlock.NotReleased => "هنوز توسط برنامه‌ریزی آزاد نشده است",
        ERP.MES.Application.Production.QueueBlock.WaitingPreviousQc => "منتظر تأیید کنترل کیفیت مرحلهٔ قبل",
        ERP.MES.Application.Production.QueueBlock.WaitingQc => "منتظر تأیید کنترل کیفیت",
        ERP.MES.Application.Production.QueueBlock.WaitingShortfallDecision => "کسری ناشی از ضایعات منتظر تصمیم مدیریت یا برنامه‌ریزی است",
        _ => "—",
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

using ERP.MES.Application.Reporting;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Tests;

// Owner decision B1 (2026-11-14): a job report entered more than five WORKING days after its work date gets a warning; the days are counted on the unit's production calendar and never refuse the report.
public class LateJobReportTests
{
    private static readonly DateTime Created = new(2026, 10, 20, 5, 0, 0, DateTimeKind.Utc);
    private static readonly IJalaliDateService Dates = new JalaliDateService(DigitStyle.Latin, TimeZoneInfo.Utc);

    private static WorkingCalendar Calendar(params DateOnly[] holidays)
    {
        var weekly = Enum.GetValues<DayOfWeek>().Select(d => new WeeklyWorkday(d, d != DayOfWeek.Friday, Created)).ToList();
        var template = new ShiftTemplate(1, "D", "Day", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), 1, Created);
        var unitShift = new UnitShift(1, 7, 1, 3, 1, false, 1, Created);
        var exceptions = holidays.Select((d, i) => new CalendarException(i + 1, d, CalendarExceptionKind.Off, null, null, "closure", 1, Created));
        return new WorkingCalendar(weekly, exceptions, [unitShift], [template]);
    }

    private static DailyJobReport Report(DateOnly workDate, DateTime? submittedUtc)
    {
        var report = new DailyJobReport(1, 1, workDate, 7, 1, 1, Created);
        if (submittedUtc is { } at)
        {
            var start = workDate.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc);
            report.ReplaceLines([new JobReportLine(1, 1, 1, 1, 1, start, start.AddHours(2), 0, null, 1, null)]);
            report.Submit(1, at, new Dictionary<int, JobLineFlags>());
        }

        return report;
    }

    [Fact]
    public void Working_days_are_counted_after_the_work_date_on_the_unit_calendar_and_the_boundary_is_five()
    {
        var saturday = new DateOnly(2026, 10, 24);
        var calendar = Calendar();

        // Sunday to Thursday are the five working days after the Saturday; the Friday is off.
        Assert.Equal(5, JobReportService.WorkingDaysLate(Report(saturday, new DateTime(2026, 10, 29, 6, 0, 0, DateTimeKind.Utc)), calendar, Dates, Created));
        Assert.Equal(5, JobReportService.WorkingDaysLate(Report(saturday, new DateTime(2026, 10, 30, 6, 0, 0, DateTimeKind.Utc)), calendar, Dates, Created)); // Friday adds nothing
        Assert.Equal(6, JobReportService.WorkingDaysLate(Report(saturday, new DateTime(2026, 10, 31, 6, 0, 0, DateTimeKind.Utc)), calendar, Dates, Created));
        Assert.Equal(0, JobReportService.WorkingDaysLate(Report(saturday, new DateTime(2026, 10, 24, 15, 0, 0, DateTimeKind.Utc)), calendar, Dates, Created)); // same day
        Assert.True(new JobReportItem(default!, string.Empty, false, false, false, 6).IsLate);
        Assert.False(new JobReportItem(default!, string.Empty, false, false, false, JobReportService.LateAfterWorkingDays).IsLate);
    }

    [Fact]
    public void A_holiday_is_not_a_working_day_and_a_draft_is_measured_to_today()
    {
        var saturday = new DateOnly(2026, 10, 24);
        var withHoliday = Calendar(new DateOnly(2026, 10, 28)); // a Wednesday closure
        Assert.Equal(4, JobReportService.WorkingDaysLate(Report(saturday, new DateTime(2026, 10, 29, 6, 0, 0, DateTimeKind.Utc)), withHoliday, Dates, Created));
        Assert.Equal(5, JobReportService.WorkingDaysLate(Report(saturday, new DateTime(2026, 10, 31, 6, 0, 0, DateTimeKind.Utc)), withHoliday, Dates, Created));

        // A draft has no submission yet: it is measured to the moment of the call.
        var draft = Report(saturday, null);
        Assert.Equal(3, JobReportService.WorkingDaysLate(draft, Calendar(), Dates, new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc)));
    }
}

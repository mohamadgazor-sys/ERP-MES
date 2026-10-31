using ERP.MES.Domain.Production;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The work session tables (Decision Log #112): seeded pause reasons, round trip of the aggregate, the database's own refusal of double-booking, append-only history, and a migration that adds only new tables.
public class WorkSessionPersistenceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateTime T0 = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int minutes) => T0.AddMinutes(minutes);

    private const string Stamp = "'2026-10-15 09:30:15'";

    // A migrated database with the minimum a session points at: a user, a request, an item, a stage, a unit, two machines and two operators.
    private static async Task<TempDatabase> SeededAsync()
    {
        var db = new TempDatabase();
        await SqliteDatabase.MigrateAsync(db.Path, Token);
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {Stamp}, {Stamp}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'C', 1, 1, {Stamp}, {Stamp}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {Stamp}, {Stamp}, 1)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, ItemSequence, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'A', 1, 'Ring', 10, 1, 1, {Stamp}, {Stamp}, 1)");
        await using var context = db.NewContext();
        context.ProductionUnits.Add(new ERP.MES.Domain.Planning.ProductionUnit(0, "MILLING", "فرز", 1, At(0)));
        await context.SaveChangesAsync(Token);
        context.Machines.Add(new ERP.MES.Domain.Planning.Machine(0, "M1", "ماشین ۱", context.ProductionUnits.Single().Id, 1, At(0)));
        context.Machines.Add(new ERP.MES.Domain.Planning.Machine(0, "M2", "ماشین ۲", context.ProductionUnits.Single().Id, 1, At(0)));
        context.Operators.Add(new ERP.MES.Domain.Planning.Operator(0, "OP1", "الف", null, 1, At(0)));
        context.Operators.Add(new ERP.MES.Domain.Planning.Operator(0, "OP2", "ب", null, 1, At(0)));
        await context.SaveChangesAsync(Token);
        db.Execute($"INSERT INTO ItemStages (Id, RequestItemId, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status, ProductionUnitId, AwaitingQcBaseUnits, AcceptedBaseUnits, ReworkBaseUnits, ScrapBaseUnits, Version) VALUES (1, 1, 1, 'S1', 2, 10, 3, 1, 0, 0, 0, 0, 1)");
        return db;
    }

    private static WorkSession NewSession(long machine = 1, long op = 1) => WorkSession.Dispatch(1, 1, 1, machine, op, null, false, null, 1, At(-5));

    [Fact]
    public async Task The_seven_owner_pause_reasons_are_seeded_and_other_requires_a_note()
    {
        using var db = await SeededAsync();
        await using var context = db.NewContext();

        var reasons = await context.PauseReasons.AsNoTracking().OrderBy(r => r.Id).ToListAsync(Token);

        Assert.Equal(
            ["POWER_OUTAGE", "PLANNED_MAINTENANCE", "MACHINE_BREAKDOWN", "OPERATOR_ABSENCE_HANDOVER", "MATERIAL_UNAVAILABLE", "WAITING_ASSISTANCE", "OTHER"],
            reasons.Select(r => r.Code).ToArray());
        Assert.All(reasons, r => Assert.True(r.IsSystem && r.IsActive));
        Assert.Equal(["OTHER"], reasons.Where(r => r.RequiresNote).Select(r => r.Code).ToArray());
        Assert.Equal(PauseScope.Unit, reasons[0].DefaultScope);
    }

    [Fact]
    public async Task A_session_with_intervals_pauses_events_and_corrections_round_trips()
    {
        using var db = await SeededAsync();
        long id;
        await using (var context = db.NewContext())
        {
            var s = NewSession();
            s.Start(1, At(0));
            s.Pause(3, false, null, PauseScope.Machine, null, 1, At(60));
            s.Resume(1, At(90));
            s.Finish(SessionFinishKind.Completed, "تمام", 1, At(150));
            s.Correct(SessionBoundary.PauseEnd, 1, At(85), "اصلاح", 1, At(200), At(300));
            context.WorkSessions.Add(s);
            await context.SaveChangesAsync(Token);
            id = s.Id;
        }

        await using var read = db.NewContext();
        var loaded = await read.WorkSessions.Include(x => x.Intervals).Include(x => x.Pauses).Include(x => x.Events).Include(x => x.Corrections).AsSplitQuery().SingleAsync(x => x.Id == id, Token);
        Assert.Equal(WorkSessionStatus.Finished, loaded.Status);
        Assert.Equal(2, loaded.Intervals.Count);
        Assert.Equal(At(85), loaded.Pauses.Single().ResumedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.Intervals[0].StartedAtUtc.Kind);
        Assert.Equal(6, loaded.Events.Count);
        Assert.Single(loaded.Corrections);
        Assert.Equal(1L, read.GetRowVersion(loaded));
    }

    [Fact]
    public async Task Timestamps_keep_sub_second_precision()
    {
        using var db = await SeededAsync();
        var precise = At(0).AddTicks(1234567);
        long id;
        await using (var context = db.NewContext())
        {
            var s = NewSession();
            s.Start(1, precise);
            context.WorkSessions.Add(s);
            await context.SaveChangesAsync(Token);
            id = s.Id;
        }

        await using var read = db.NewContext();
        Assert.Equal(precise, (await read.WorkSessionIntervals.SingleAsync(Token)).StartedAtUtc);
        Assert.Equal(id, (await read.WorkSessions.SingleAsync(Token)).Id);
    }

    [Fact]
    public async Task The_database_refuses_a_second_open_interval_on_the_same_machine_or_for_the_same_operator()
    {
        using var db = await SeededAsync();
        await using (var context = db.NewContext())
        {
            var first = NewSession(1, 1);
            first.Start(1, At(0));
            context.WorkSessions.Add(first);
            await context.SaveChangesAsync(Token);
        }

        await using (var sameMachine = db.NewContext())
        {
            var other = NewSession(1, 2);
            other.Start(1, At(1));
            sameMachine.WorkSessions.Add(other);
            await Assert.ThrowsAsync<DbUpdateException>(() => sameMachine.SaveChangesAsync(Token));
        }

        await using (var sameOperator = db.NewContext())
        {
            var other = NewSession(2, 1);
            other.Start(1, At(1));
            sameOperator.WorkSessions.Add(other);
            await Assert.ThrowsAsync<DbUpdateException>(() => sameOperator.SaveChangesAsync(Token));
        }

        await using (var free = db.NewContext())
        {
            var other = NewSession(2, 2);
            other.Start(1, At(1));
            free.WorkSessions.Add(other);
            await free.SaveChangesAsync(Token);
        }
    }

    [Fact]
    public async Task A_paused_session_frees_the_machine_and_a_handover_reuses_it_in_two_steps()
    {
        using var db = await SeededAsync();
        await using var context = db.NewContext();
        var first = NewSession(1, 1);
        first.Start(1, At(0));
        context.WorkSessions.Add(first);
        await context.SaveChangesAsync(Token);

        first.Finish(SessionFinishKind.HandedOver, null, 1, At(120));
        await context.SaveChangesAsync(Token);
        var successor = WorkSession.Dispatch(1, 1, 1, 1, 2, first.Id, false, null, 1, At(120));
        successor.Start(1, At(120));
        context.WorkSessions.Add(successor);
        await context.SaveChangesAsync(Token);

        Assert.Equal(2, await context.WorkSessions.CountAsync(Token));
        Assert.Equal(1, await context.WorkSessionIntervals.CountAsync(i => i.EndedAtUtc == null, Token));
        Assert.Equal(first.Id, (await context.WorkSessions.SingleAsync(s => s.OperatorId == 2, Token)).PredecessorSessionId);
    }

    [Fact]
    public async Task Session_history_and_corrections_are_append_only()
    {
        using var db = await SeededAsync();
        await using var context = db.NewContext();
        var s = NewSession();
        s.Start(1, At(0));
        s.Finish(SessionFinishKind.Completed, null, 1, At(10));
        s.Correct(SessionBoundary.Finish, 0, At(12), "x", 1, At(20), At(30));
        context.WorkSessions.Add(s);
        await context.SaveChangesAsync(Token);

        var ev = await context.WorkSessionEvents.FirstAsync(Token);
        context.Entry(ev).Property(e => e.Detail).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        context.ChangeTracker.Clear();
        var correction = await context.WorkSessionCorrections.FirstAsync(Token);
        context.WorkSessionCorrections.Remove(correction);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task A_stale_session_version_is_refused()
    {
        using var db = await SeededAsync();
        long id;
        await using (var context = db.NewContext())
        {
            var s = NewSession();
            context.WorkSessions.Add(s);
            await context.SaveChangesAsync(Token);
            id = s.Id;
        }

        await using var a = db.NewContext();
        await using var b = db.NewContext();
        var first = await a.WorkSessions.Include(x => x.Intervals).Include(x => x.Events).AsSplitQuery().SingleAsync(x => x.Id == id, Token);
        var second = await b.WorkSessions.Include(x => x.Intervals).Include(x => x.Events).AsSplitQuery().SingleAsync(x => x.Id == id, Token);
        first.Start(1, At(0));
        await a.SaveChangesAsync(Token);
        second.Cancel("x", 1, At(0));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Existing_job_reports_and_stages_survive_the_migration_unchanged_and_the_session_tables_start_empty()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddWorkSessions", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {Stamp}, {Stamp}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'C', 1, 1, {Stamp}, {Stamp}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {Stamp}, {Stamp}, 7)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'A', 'Ring', 10, 1, 1, {Stamp}, {Stamp}, 3)");
        db.Execute($"INSERT INTO ItemStages (Id, RequestItemId, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status, AwaitingQcBaseUnits, AcceptedBaseUnits, ReworkBaseUnits, ScrapBaseUnits, Version) VALUES (1, 1, 1, 'S1', 2, 10, 4, 0, 0, 0, 0, 5)");
        var stagesBefore = db.Query("SELECT Id, Status, Version FROM ItemStages");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal(stagesBefore, db.Query("SELECT Id, Status, Version FROM ItemStages"));
        Assert.Empty(db.Query("SELECT * FROM WorkSessions"));
        Assert.Empty(db.Query("SELECT * FROM WorkSessionIntervals"));
        Assert.Equal(7, db.Query("SELECT * FROM PauseReasons").Count);
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}

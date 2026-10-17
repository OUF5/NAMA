using System.Globalization;
using Api.Domain;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace Api.Infrastructure;

// PRD 7.2: one FirestoreDb singleton. FIRESTORE_EMULATOR_HOST switches it to the emulator (PRD 7.11).
// It is built on first use, so tests that replace IUserStore never need Google credentials.
public static class FirestoreSetup
{
    public static IServiceCollection AddFirestore(this IServiceCollection services)
    {
        services.AddSingleton(provider => new FirestoreDbBuilder
        {
            ProjectId = provider.GetRequiredService<IConfiguration>()["FIREBASE_PROJECT_ID"],
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.Build());
        services.AddSingleton<IUserStore, FirestoreUserStore>();
        return services;
    }
}

public sealed class FirestoreUserStore(FirestoreDb db) : IUserStore
{
    private DocumentReference User(string uid) => db.Collection("users").Document(uid);

    public async Task<UserProfile?> GetProfileAsync(string uid)
    {
        var snapshot = await User(uid).GetSnapshotAsync();
        return snapshot.Exists ? FirestoreDocuments.ToProfile(snapshot.ConvertTo<ProfileDocument>()) : null;
    }

    public async Task<IReadOnlyList<FiscalYear>> GetFiscalYearsAsync(string uid)
    {
        var snapshot = await User(uid).Collection("fiscalYears").GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToFiscalYear(document.Id, document.ConvertTo<FiscalYearDocument>()))
            .ToList();
    }

    // SET-02: one atomic batch. The profile is created, not set, so a second setup fails as a whole.
    public async Task<bool> CreateSetupAsync(string uid, SetupData setup)
    {
        var user = User(uid);
        var batch = db.StartBatch();
        batch.Create(user, FirestoreDocuments.ToDocument(setup.Profile));
        batch.Create(user.Collection("fiscalYears").Document(setup.FiscalYear.Id), FirestoreDocuments.ToDocument(setup.FiscalYear));
        foreach (var income in setup.Incomes)
        {
            batch.Create(user.Collection("incomes").Document(income.Id), FirestoreDocuments.ToDocument(income));
        }
        foreach (var account in setup.BankAccounts)
        {
            batch.Create(user.Collection("bankAccounts").Document(account.Id), FirestoreDocuments.ToDocument(account));
        }
        foreach (var category in setup.Categories)
        {
            batch.Create(user.Collection("categories").Document(category.Id), FirestoreDocuments.ToDocument(category));
        }
        try
        {
            await batch.CommitAsync();
            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.AlreadyExists)
        {
            return false;
        }
    }
}

// PRD 7.4 documents: camelCase fields, long minor units, YYYY-MM-DD date strings,
// and createdAt / updatedAt set by the server.
[FirestoreData]
public sealed class ProfileDocument
{
    [FirestoreProperty("displayName")] public string DisplayName { get; set; } = "";
    [FirestoreProperty("email")] public string Email { get; set; } = "";
    [FirestoreProperty("projectName")] public string ProjectName { get; set; } = "";
    [FirestoreProperty("currency")] public string Currency { get; set; } = "";
    [FirestoreProperty("timezone")] public string Timezone { get; set; } = "";
    [FirestoreProperty("fiscalStartMonth")] public int FiscalStartMonth { get; set; }
    [FirestoreProperty("paydayDay")] public int PaydayDay { get; set; }
    [FirestoreProperty("setupCompleted")] public bool SetupCompleted { get; set; }
    [FirestoreProperty("lastBankAccountId")] public string? LastBankAccountId { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class FiscalYearDocument
{
    [FirestoreProperty("label")] public string Label { get; set; } = "";
    [FirestoreProperty("startDate")] public string StartDate { get; set; } = "";
    [FirestoreProperty("endDate")] public string EndDate { get; set; } = "";
    [FirestoreProperty("fiscalStartMonth")] public int FiscalStartMonth { get; set; }
    [FirestoreProperty("paydayDay")] public int PaydayDay { get; set; }
    [FirestoreProperty("isTransition")] public bool IsTransition { get; set; }
    [FirestoreProperty("periods")] public List<PeriodDocument> Periods { get; set; } = [];
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class PeriodDocument
{
    [FirestoreProperty("key")] public string Key { get; set; } = "";
    [FirestoreProperty("label")] public string Label { get; set; } = "";
    [FirestoreProperty("startDate")] public string StartDate { get; set; } = "";
    [FirestoreProperty("endDate")] public string EndDate { get; set; } = "";
}

[FirestoreData]
public sealed class IncomeDocument
{
    [FirestoreProperty("kind")] public string Kind { get; set; } = "";
    [FirestoreProperty("label")] public string Label { get; set; } = "";
    [FirestoreProperty("amountMinor")] public long AmountMinor { get; set; }
    [FirestoreProperty("fromPeriodKey")] public string FromPeriodKey { get; set; } = "";
    [FirestoreProperty("toPeriodKey")] public string? ToPeriodKey { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class BankAccountDocument
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("nameLower")] public string NameLower { get; set; } = "";
    [FirestoreProperty("archived")] public bool Archived { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class CategoryDocument
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("nameLower")] public string NameLower { get; set; } = "";
    [FirestoreProperty("sortOrder")] public int SortOrder { get; set; }
    [FirestoreProperty("archived")] public bool Archived { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

// Domain ↔ document. Dates become YYYY-MM-DD strings only here (PRD 7.4).
public static class FirestoreDocuments
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string ToDateString(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly ToDate(string value) => DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    public static ProfileDocument ToDocument(UserProfile profile) => new()
    {
        DisplayName = profile.DisplayName,
        Email = profile.Email,
        ProjectName = profile.ProjectName,
        Currency = profile.Currency,
        Timezone = profile.Timezone,
        FiscalStartMonth = profile.FiscalStartMonth,
        PaydayDay = profile.PaydayDay,
        SetupCompleted = profile.SetupCompleted,
        LastBankAccountId = profile.LastBankAccountId,
    };

    public static UserProfile ToProfile(ProfileDocument document) => new(
        document.DisplayName, document.Email, document.ProjectName, document.Currency, document.Timezone,
        document.FiscalStartMonth, document.PaydayDay, document.SetupCompleted, document.LastBankAccountId);

    public static FiscalYearDocument ToDocument(FiscalYear fiscalYear) => new()
    {
        Label = fiscalYear.Label,
        StartDate = ToDateString(fiscalYear.StartDate),
        EndDate = ToDateString(fiscalYear.EndDate),
        FiscalStartMonth = fiscalYear.FiscalStartMonth,
        PaydayDay = fiscalYear.PaydayDay,
        IsTransition = fiscalYear.IsTransition,
        Periods = fiscalYear.Periods.Select(period => new PeriodDocument
        {
            Key = period.Key,
            Label = period.Label,
            StartDate = ToDateString(period.StartDate),
            EndDate = ToDateString(period.EndDate),
        }).ToList(),
    };

    // R-CAL-09: the stored months are used as they are, never recomputed.
    public static FiscalYear ToFiscalYear(string id, FiscalYearDocument document) => new(
        id, document.Label, ToDate(document.StartDate), ToDate(document.EndDate),
        document.FiscalStartMonth, document.PaydayDay, document.IsTransition,
        document.Periods
            .Select(period => new Period(period.Key, period.Label, ToDate(period.StartDate), ToDate(period.EndDate)))
            .ToList()
            .AsReadOnly());

    public static IncomeDocument ToDocument(IncomeItem income) => new()
    {
        Kind = income.Kind == IncomeKind.Salary ? "salary" : "additional",
        Label = income.Label,
        AmountMinor = income.AmountMinor,
        FromPeriodKey = income.FromPeriodKey,
        ToPeriodKey = income.ToPeriodKey,
    };

    public static BankAccountDocument ToDocument(BankAccount account) => new()
    {
        Name = account.Name,
        NameLower = Names.Lower(account.Name),
    };

    public static CategoryDocument ToDocument(Category category) => new()
    {
        Name = category.Name,
        NameLower = Names.Lower(category.Name),
        SortOrder = category.SortOrder,
    };
}

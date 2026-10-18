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

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(string uid)
    {
        var snapshot = await User(uid).Collection("categories").GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToCategory(document.Id, document.ConvertTo<CategoryDocument>()))
            .ToList();
    }

    public async Task<IReadOnlyList<SubCategory>> GetSubCategoriesAsync(string uid)
    {
        var snapshot = await User(uid).Collection("subCategories").GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToSubCategory(document.Id, document.ConvertTo<SubCategoryDocument>()))
            .ToList();
    }

    public Task AddCategoryAsync(string uid, Category category) =>
        User(uid).Collection("categories").Document(category.Id).CreateAsync(FirestoreDocuments.ToDocument(category));

    public Task AddSubCategoryAsync(string uid, SubCategory subCategory) =>
        User(uid).Collection("subCategories").Document(subCategory.Id).CreateAsync(FirestoreDocuments.ToDocument(subCategory));

    public async Task<IReadOnlyList<PlanAmounts>> GetPlansAsync(string uid, string fyId)
    {
        var snapshot = await User(uid).Collection("plans").WhereEqualTo("fyId", fyId).GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToPlan(document.ConvertTo<PlanDocument>()))
            .ToList();
    }

    // PRD 7.4 plans/{fyId}_{subCategoryId}. An existing plan only gets new amounts and updatedAt,
    // so createdAt is kept.
    public Task SetPlanAsync(string uid, string fyId, PlanAmounts plan)
    {
        var reference = User(uid).Collection("plans").Document($"{fyId}_{plan.SubCategoryId}");
        return db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference);
            if (snapshot.Exists)
            {
                transaction.Update(reference, new Dictionary<string, object>
                {
                    ["amounts"] = plan.Amounts.ToDictionary(),
                    ["updatedAt"] = FieldValue.ServerTimestamp,
                });
            }
            else
            {
                transaction.Create(reference, FirestoreDocuments.ToDocument(fyId, plan));
            }
        });
    }

    public async Task<IReadOnlyList<IncomeItem>> GetIncomesAsync(string uid)
    {
        var snapshot = await User(uid).Collection("incomes").GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToIncome(document.Id, document.ConvertTo<IncomeDocument>()))
            .ToList();
    }

    public async Task<IReadOnlyList<BankAccount>> GetBankAccountsAsync(string uid)
    {
        var snapshot = await User(uid).Collection("bankAccounts").GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToBankAccount(document.Id, document.ConvertTo<BankAccountDocument>()))
            .ToList();
    }

    // Decision 5: the entry and the profile's lastBankAccountId are written together.
    public Task AddEntryAsync(string uid, Entry entry)
    {
        var user = User(uid);
        var batch = db.StartBatch();
        batch.Create(user.Collection("entries").Document(entry.Id), FirestoreDocuments.ToDocument(entry));
        batch.Update(user, new Dictionary<string, object>
        {
            ["lastBankAccountId"] = entry.BankAccountId,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        });
        return batch.CommitAsync();
    }

    // Equality filters only, so single-field indexes serve it (no composite index).
    public async Task<IReadOnlyList<Entry>> GetEntriesAsync(string uid, string subCategoryId, string periodKey)
    {
        var snapshot = await User(uid).Collection("entries")
            .WhereEqualTo("subCategoryId", subCategoryId)
            .WhereEqualTo("periodKey", periodKey)
            .GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => FirestoreDocuments.ToEntry(document.Id, document.ConvertTo<EntryDocument>()))
            .ToList();
    }

    public async Task<Entry?> GetEntryAsync(string uid, string id)
    {
        var snapshot = await User(uid).Collection("entries").Document(id).GetSnapshotAsync();
        return snapshot.Exists ? FirestoreDocuments.ToEntry(snapshot.Id, snapshot.ConvertTo<EntryDocument>()) : null;
    }

    // PRD 7.4: one query per FY, served by the entries (fyId asc, date desc) index in
    // firestore.indexes.json. Same-date entries come back added-later first.
    public async Task<IReadOnlyList<Entry>> GetFiscalYearEntriesAsync(string uid, string fyId)
    {
        var snapshot = await User(uid).Collection("entries")
            .WhereEqualTo("fyId", fyId)
            .OrderByDescending("date")
            .GetSnapshotAsync();
        return snapshot.Documents
            .Select(document => (document.Id, Document: document.ConvertTo<EntryDocument>()))
            .OrderByDescending(item => item.Document.Date, StringComparer.Ordinal)
            .ThenByDescending(item => item.Document.CreatedAt)
            .Select(item => FirestoreDocuments.ToEntry(item.Id, item.Document))
            .ToList();
    }

    // UpdateAsync requires the document to exist (Precondition.MustExist), so createdAt is kept.
    public async Task<bool> UpdateEntryAsync(string uid, Entry entry)
    {
        var document = FirestoreDocuments.ToDocument(entry);
        try
        {
            await User(uid).Collection("entries").Document(entry.Id).UpdateAsync(new Dictionary<string, object>
            {
                ["date"] = document.Date,
                ["periodKey"] = document.PeriodKey,
                ["fyId"] = document.FyId,
                ["categoryId"] = document.CategoryId,
                ["subCategoryId"] = document.SubCategoryId,
                ["bankAccountId"] = document.BankAccountId,
                ["amountMinor"] = document.AmountMinor,
                ["note"] = document.Note,
                ["updatedAt"] = FieldValue.ServerTimestamp,
            });
            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }

    public Task<bool> DeleteEntryAsync(string uid, string id) => DeleteAsync(User(uid).Collection("entries").Document(id));

    public Task UpdateProfileAsync(string uid, string displayName, string projectName, string currency, string timezone) =>
        User(uid).UpdateAsync(new Dictionary<string, object>
        {
            ["displayName"] = displayName,
            ["projectName"] = projectName,
            ["currency"] = currency,
            ["timezone"] = timezone,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        });

    public Task UpdateCalendarAsync(string uid, int fiscalStartMonth, int paydayDay) =>
        User(uid).UpdateAsync(new Dictionary<string, object>
        {
            ["fiscalStartMonth"] = fiscalStartMonth,
            ["paydayDay"] = paydayDay,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        });

    // FY-02: the FY document is created, not set, so a second start of the same FY fails as a whole.
    public async Task<bool> StartFiscalYearAsync(string uid, FiscalYear fiscalYear, IReadOnlyList<PlanAmounts> plans)
    {
        var user = User(uid);
        var batch = db.StartBatch();
        batch.Create(user.Collection("fiscalYears").Document(fiscalYear.Id), FirestoreDocuments.ToDocument(fiscalYear));
        foreach (var plan in plans)
        {
            batch.Create(user.Collection("plans").Document($"{fiscalYear.Id}_{plan.SubCategoryId}"), FirestoreDocuments.ToDocument(fiscalYear.Id, plan));
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

    // Ended items keep createdAt (Update); replaced items are deleted (owner's rule, M2).
    public Task ChangeSalaryAsync(string uid, IReadOnlyList<IncomeItem> ended, IReadOnlyList<string> replacedIds, IncomeItem created)
    {
        var incomes = User(uid).Collection("incomes");
        var batch = db.StartBatch();
        foreach (var item in ended)
        {
            batch.Update(incomes.Document(item.Id), new Dictionary<string, object>
            {
                // An ended item always has its last month (R-INC-02).
                ["toPeriodKey"] = item.ToPeriodKey!,
                ["updatedAt"] = FieldValue.ServerTimestamp,
            });
        }
        foreach (var id in replacedIds)
        {
            batch.Delete(incomes.Document(id));
        }
        batch.Create(incomes.Document(created.Id), FirestoreDocuments.ToDocument(created));
        return batch.CommitAsync();
    }

    public Task AddIncomeAsync(string uid, IncomeItem income) =>
        User(uid).Collection("incomes").Document(income.Id).CreateAsync(FirestoreDocuments.ToDocument(income));

    public Task<bool> UpdateIncomeAsync(string uid, IncomeItem income)
    {
        var document = FirestoreDocuments.ToDocument(income);
        return UpdateAsync(User(uid).Collection("incomes").Document(income.Id), new Dictionary<string, object>
        {
            ["label"] = document.Label,
            ["amountMinor"] = document.AmountMinor,
            ["fromPeriodKey"] = document.FromPeriodKey,
            // null is stored as null: open ended (PRD 7.4).
            ["toPeriodKey"] = document.ToPeriodKey!,
        });
    }

    public Task<bool> DeleteIncomeAsync(string uid, string id) => DeleteAsync(User(uid).Collection("incomes").Document(id));

    public Task AddBankAccountAsync(string uid, BankAccount account) =>
        User(uid).Collection("bankAccounts").Document(account.Id).CreateAsync(FirestoreDocuments.ToDocument(account));

    public Task<bool> UpdateBankAccountAsync(string uid, BankAccount account) =>
        UpdateAsync(User(uid).Collection("bankAccounts").Document(account.Id), NameFields(account.Name, account.Archived));

    public Task<bool> UpdateCategoryAsync(string uid, Category category) =>
        UpdateAsync(User(uid).Collection("categories").Document(category.Id), NameFields(category.Name, category.Archived));

    public Task<bool> UpdateSubCategoryAsync(string uid, SubCategory subCategory) =>
        UpdateAsync(User(uid).Collection("subCategories").Document(subCategory.Id), NameFields(subCategory.Name, subCategory.Archived));

    private static Dictionary<string, object> NameFields(string name, bool archived) => new()
    {
        ["name"] = name,
        ["nameLower"] = Names.Lower(name),
        ["archived"] = archived,
    };

    // UpdateAsync requires the document to exist (Precondition.MustExist), so createdAt is kept.
    private static async Task<bool> UpdateAsync(DocumentReference reference, Dictionary<string, object> fields)
    {
        fields["updatedAt"] = FieldValue.ServerTimestamp;
        try
        {
            await reference.UpdateAsync(fields);
            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }

    private static async Task<bool> DeleteAsync(DocumentReference reference)
    {
        try
        {
            await reference.DeleteAsync(Precondition.MustExist);
            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
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

[FirestoreData]
public sealed class SubCategoryDocument
{
    [FirestoreProperty("categoryId")] public string CategoryId { get; set; } = "";
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("nameLower")] public string NameLower { get; set; } = "";
    [FirestoreProperty("sortOrder")] public int SortOrder { get; set; }
    [FirestoreProperty("archived")] public bool Archived { get; set; }
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class PlanDocument
{
    [FirestoreProperty("fyId")] public string FyId { get; set; } = "";
    [FirestoreProperty("subCategoryId")] public string SubCategoryId { get; set; } = "";
    [FirestoreProperty("categoryId")] public string CategoryId { get; set; } = "";
    // Month key → amount in minor units (R-MON-01).
    [FirestoreProperty("amounts")] public Dictionary<string, long> Amounts { get; set; } = [];
    [FirestoreProperty("createdAt"), ServerTimestamp] public Timestamp CreatedAt { get; set; }
    [FirestoreProperty("updatedAt"), ServerTimestamp] public Timestamp UpdatedAt { get; set; }
}

[FirestoreData]
public sealed class EntryDocument
{
    [FirestoreProperty("date")] public string Date { get; set; } = "";
    [FirestoreProperty("periodKey")] public string PeriodKey { get; set; } = "";
    [FirestoreProperty("fyId")] public string FyId { get; set; } = "";
    [FirestoreProperty("categoryId")] public string CategoryId { get; set; } = "";
    [FirestoreProperty("subCategoryId")] public string SubCategoryId { get; set; } = "";
    [FirestoreProperty("bankAccountId")] public string BankAccountId { get; set; } = "";
    [FirestoreProperty("amountMinor")] public long AmountMinor { get; set; }
    [FirestoreProperty("note")] public string Note { get; set; } = "";
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
        Archived = account.Archived,
    };

    public static BankAccount ToBankAccount(string id, BankAccountDocument document) =>
        new(id, document.Name, document.Archived);

    public static EntryDocument ToDocument(Entry entry) => new()
    {
        Date = ToDateString(entry.Date),
        PeriodKey = entry.PeriodKey,
        FyId = entry.FyId,
        CategoryId = entry.CategoryId,
        SubCategoryId = entry.SubCategoryId,
        BankAccountId = entry.BankAccountId,
        AmountMinor = entry.AmountMinor,
        Note = entry.Note,
    };

    public static Entry ToEntry(string id, EntryDocument document) => new(
        id, ToDate(document.Date), document.PeriodKey, document.FyId, document.CategoryId,
        document.SubCategoryId, document.BankAccountId, document.AmountMinor, document.Note);

    public static IncomeItem ToIncome(string id, IncomeDocument document) => new(
        id, document.Kind == "salary" ? IncomeKind.Salary : IncomeKind.Additional, document.Label,
        document.AmountMinor, document.FromPeriodKey, document.ToPeriodKey);

    public static CategoryDocument ToDocument(Category category) => new()
    {
        Name = category.Name,
        NameLower = Names.Lower(category.Name),
        SortOrder = category.SortOrder,
        Archived = category.Archived,
    };

    public static Category ToCategory(string id, CategoryDocument document) =>
        new(id, document.Name, document.SortOrder, document.Archived);

    public static SubCategoryDocument ToDocument(SubCategory subCategory) => new()
    {
        CategoryId = subCategory.CategoryId,
        Name = subCategory.Name,
        NameLower = Names.Lower(subCategory.Name),
        SortOrder = subCategory.SortOrder,
        Archived = subCategory.Archived,
    };

    public static SubCategory ToSubCategory(string id, SubCategoryDocument document) =>
        new(id, document.CategoryId, document.Name, document.SortOrder, document.Archived);

    public static PlanDocument ToDocument(string fyId, PlanAmounts plan) => new()
    {
        FyId = fyId,
        SubCategoryId = plan.SubCategoryId,
        CategoryId = plan.CategoryId,
        Amounts = plan.Amounts.ToDictionary(),
    };

    public static PlanAmounts ToPlan(PlanDocument document) =>
        new(document.SubCategoryId, document.CategoryId, document.Amounts);
}

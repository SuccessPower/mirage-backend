using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Persistence;

public sealed record ChurchSyncResult(int Created, int Skipped, int BranchesCreated, int DenominationsUpdated, int Retired);

// Shared by the automatic startup sweep (DatabaseInitialiser, runs on every deploy with no
// human owner available) and the manual "Seed starter churches" admin button (which does have a
// PlatformAdmin to assign as owner for brand-new churches) — both need to reconcile the database
// against SeedData/nigerian-churches.json the same way, so the logic lives in one place.
public static class ChurchDirectorySync
{
    // Churches curated out of the starter directory as no longer fitting the platform's
    // denomination lineup. Kept as a fixed list (rather than "anything missing from the JSON")
    // so a sync never touches an independently-created org that just isn't in the seed file.
    private static readonly string[] RetiredChurchNames =
    [
        "Celestial Church of Christ",
        "Cherubim and Seraphim Movement Church",
        "Brotherhood of the Cross and Star",
        "The Synagogue, Church of All Nations (SCOAN)"
    ];

    // Corrects the denomination on existing rows whose JSON value has since changed, and retires
    // (suspends, not deletes — an Organisation may already have branches/members/events attached
    // with no cascade-delete path) any existing church dropped from the curated list. When
    // `actorId` is supplied, also creates any church from the JSON not already present by name
    // (pre-approved, owned by `actorId` — real church admins are added afterwards via the existing
    // invite flow, since ownership can't be transferred once set); when null (no PlatformAdmin
    // context available, e.g. at startup), creation is skipped and only sync/retire run. Safe to
    // re-run any time — insert/update/retire are all matched by exact name, so it's idempotent.
    public static async Task<ChurchSyncResult> SyncAsync(MirageDbContext db, string seedDataPath, Guid? actorId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(seedDataPath))
            return new ChurchSyncResult(0, 0, 0, 0, 0);

        var json = await File.ReadAllTextAsync(seedDataPath, cancellationToken);
        var entries = JsonSerializer.Deserialize<List<ChurchSeedEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var existingOrgs = await db.Organisations.ToListAsync(cancellationToken);
        var existingByName = new Dictionary<string, Organisation>(
            existingOrgs.ToDictionary(x => x.Name, x => x), StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, branchesCreated = 0, denominationsUpdated = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;

            if (existingByName.TryGetValue(entry.Name, out var existing))
            {
                skipped++;
                if (!string.IsNullOrWhiteSpace(entry.Denomination) &&
                    !string.Equals(existing.Denomination, entry.Denomination, StringComparison.OrdinalIgnoreCase))
                {
                    existing.UpdateDenomination(entry.Denomination);
                    denominationsUpdated++;
                }
                continue;
            }

            if (actorId is null) continue;

            var registrationNumber = $"SEED-{Guid.NewGuid():N}";
            var organisation = new Organisation(actorId.Value, entry.Name, entry.Denomination ?? "Other",
                entry.HeadquartersCountry ?? "Nigeria", registrationNumber, entry.LogoUrl, entry.WebsiteUrl);
            organisation.Approve();
            db.Organisations.Add(organisation);

            foreach (var branch in entry.Branches ?? [])
            {
                if (string.IsNullOrWhiteSpace(branch.Name) || string.IsNullOrWhiteSpace(branch.City)) continue;
                db.OrganisationBranches.Add(new OrganisationBranch(organisation.Id, branch.Name, branch.City,
                    entry.HeadquartersCountry ?? "Nigeria", null));
                branchesCreated++;
            }

            existingByName.Add(entry.Name, organisation);
            created++;
        }

        var retired = 0;
        foreach (var org in existingOrgs)
        {
            if (RetiredChurchNames.Contains(org.Name, StringComparer.OrdinalIgnoreCase) &&
                org.Status != OrganisationStatus.Suspended)
            {
                org.Suspend();
                retired++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ChurchSyncResult(created, skipped, branchesCreated, denominationsUpdated, retired);
    }

    private sealed record ChurchSeedEntry(
        string Name,
        string? Denomination,
        string? HeadquartersCity,
        string? HeadquartersCountry,
        string? LeadPastor,
        string? LogoUrl,
        string? WebsiteUrl,
        List<ChurchSeedBranch>? Branches);

    private sealed record ChurchSeedBranch(string Name, string City);
}

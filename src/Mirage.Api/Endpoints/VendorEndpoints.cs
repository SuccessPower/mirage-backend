using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class VendorEndpoints
{
    public static RouteGroupBuilder MapVendorEndpoints(this RouteGroupBuilder api)
    {
        var vendors = api.MapGroup("/vendors").WithTags("Vendors");

        vendors.MapGet("/", ListApproved);
        vendors.MapGet("/mine", ListMine).RequireAuthorization();
        vendors.MapGet("/all", ListAll).RequireAuthorization(MiragePolicy.PlatformAdmin);
        vendors.MapGet("/pending", ListPending).RequireAuthorization(MiragePolicy.PlatformAdmin);
        vendors.MapGet("/{id:guid}", GetById);
        vendors.MapPost("/", Create).RequireAuthorization();
        vendors.MapPut("/{id:guid}", Update).RequireAuthorization();
        vendors.MapPut("/{id:guid}/photos", SetPhotos).RequireAuthorization();
        vendors.MapPatch("/{id:guid}/approve", Approve).RequireAuthorization(MiragePolicy.PlatformAdmin);
        vendors.MapPatch("/{id:guid}/reject", Reject).RequireAuthorization(MiragePolicy.PlatformAdmin);

        return api;
    }

    private static VendorResponse ToResponse(Vendor v) => new(
        v.Id, v.OwnerUserId, v.BusinessName, v.Category, v.Description, v.Email, v.Phone,
        v.Address, v.City, v.Country, v.PhotoUrls, v.Status, v.CreatedAt);

    private static async Task<IResult> ListApproved(HttpContext context, IMirageDbContext db,
        VendorCategory? category, string? city, CancellationToken cancellationToken)
    {
        var query = db.Vendors.AsNoTracking().Where(x => x.Status == VendorStatus.Approved);
        if (category is not null) query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(x => x.City == city);

        var results = await query.OrderBy(x => x.BusinessName).Select(x => ToResponse(x)).ToListAsync(cancellationToken);
        return ApiResults.Ok(context, results, "Vendors retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var vendor = await db.Vendors.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.Status == VendorStatus.Approved, cancellationToken);
        if (vendor is null) return EndpointHelpers.NotFound(context, "Vendor was not found.");
        return ApiResults.Ok(context, ToResponse(vendor), "Vendor retrieved successfully.");
    }

    private static async Task<IResult> ListMine(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var results = await db.Vendors.AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, results, "Your vendor listings were retrieved successfully.");
    }

    private static async Task<IResult> ListAll(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var results = await db.Vendors.AsNoTracking()
            .OrderBy(x => x.BusinessName)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, results, "Vendors retrieved successfully.");
    }

    private static async Task<IResult> ListPending(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var results = await db.Vendors.AsNoTracking()
            .Where(x => x.Status == VendorStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, results, "Pending vendors retrieved successfully.");
    }

    private static async Task<IResult> Create(CreateVendorRequest request, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName) || string.IsNullOrWhiteSpace(request.Description) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.City) ||
            string.IsNullOrWhiteSpace(request.Country))
            return EndpointHelpers.ValidationProblem(context,
                ("vendor", "Business name, description, email, phone, address, city, and country are required."));

        var vendor = new Vendor(context.User.GetUserId(), request.BusinessName, request.Category, request.Description,
            request.Email, request.Phone, request.Address, request.City, request.Country);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/vendors/{vendor.Id}",
            ToResponse(vendor), "Vendor listing submitted successfully.");
    }

    private static async Task<(IResult? Forbidden, Vendor? Vendor)> RequireVendorOwner(Guid id, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var vendor = await db.Vendors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (vendor is null) return (EndpointHelpers.NotFound(context, "Vendor was not found."), null);
        if (vendor.OwnerUserId != context.User.GetUserId() && !context.User.IsInRole(MirageRoles.PlatformAdmin))
            return (EndpointHelpers.Forbidden(context), null);
        return (null, vendor);
    }

    private static async Task<IResult> Update(Guid id, UpdateVendorRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var (forbidden, vendor) = await RequireVendorOwner(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;
        if (string.IsNullOrWhiteSpace(request.BusinessName) || string.IsNullOrWhiteSpace(request.Description) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.City) ||
            string.IsNullOrWhiteSpace(request.Country))
            return EndpointHelpers.ValidationProblem(context,
                ("vendor", "Business name, description, email, phone, address, city, and country are required."));

        vendor!.UpdateDetails(request.BusinessName, request.Category, request.Description, request.Email,
            request.Phone, request.Address, request.City, request.Country);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, ToResponse(vendor), "Vendor listing updated successfully.");
    }

    private static async Task<IResult> SetPhotos(Guid id, SetVendorPhotosRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var (forbidden, vendor) = await RequireVendorOwner(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        try
        {
            vendor!.SetPhotos(request.PhotoUrls);
        }
        catch (InvalidOperationException ex)
        {
            return EndpointHelpers.ValidationProblem(context, ("photoUrls", ex.Message));
        }

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, ToResponse(vendor), "Vendor photos updated successfully.");
    }

    private static async Task<IResult> Approve(Guid id, HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, NotificationService notifications, CancellationToken cancellationToken)
    {
        var vendor = await db.Vendors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (vendor is null) return EndpointHelpers.NotFound(context, "Vendor was not found.");
        if (vendor.Status == VendorStatus.Approved)
            return EndpointHelpers.Conflict(context, "Vendor is already approved.");

        var owner = await userManager.FindByIdAsync(vendor.OwnerUserId.ToString());
        if (owner is null) return EndpointHelpers.NotFound(context, "Vendor owner user was not found.");

        vendor.Approve();
        await db.SaveChangesAsync(cancellationToken);

        if (!await userManager.IsInRoleAsync(owner, MirageRoles.Vendor))
            await userManager.AddToRoleAsync(owner, MirageRoles.Vendor);

        await notifications.NotifyAsync(vendor.OwnerUserId, NotificationType.VendorApproved,
            "Vendor listing approved", $"{vendor.BusinessName} has been approved and is now live in the marketplace.",
            vendor.Id, "Vendor", cancellationToken);

        return ApiResults.Ok(context, ToResponse(vendor), "Vendor approved successfully.");
    }

    private static async Task<IResult> Reject(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var vendor = await db.Vendors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (vendor is null) return EndpointHelpers.NotFound(context, "Vendor was not found.");
        if (vendor.Status is VendorStatus.Rejected or VendorStatus.Suspended)
            return EndpointHelpers.Conflict(context, $"Vendor is already {vendor.Status.ToString().ToLower()}.");

        vendor.Reject();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(vendor.OwnerUserId, NotificationType.VendorRejected,
            "Vendor listing rejected", $"{vendor.BusinessName} was not approved.",
            vendor.Id, "Vendor", cancellationToken);

        return ApiResults.Ok(context, ToResponse(vendor), "Vendor rejected.");
    }
}

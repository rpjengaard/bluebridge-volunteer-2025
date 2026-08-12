// [CHANGE: SuperAdmin ticket sales page] Related: Code/Notifications/SuperAdminMemberGroupComposer.cs, Web/Controllers/BbvTicketSalesController.cs, Web/Controllers/TicketSalesApiController.cs, Web/Program.cs
using Umbraco.Cms.Core.Security;

namespace Code.Services;

public interface ISuperAdminService
{
    /// <summary>True when a member is logged in and belongs to the SuperAdmin member group.</summary>
    Task<bool> IsCurrentMemberSuperAdminAsync();

    /// <summary>True when the given (already loaded) member belongs to the SuperAdmin member group.</summary>
    Task<bool> IsSuperAdminAsync(MemberIdentityUser? member);
}

public class SuperAdminService : ISuperAdminService
{
    /// <summary>Name of the member group that unlocks the frontend ticket sales page.</summary>
    public const string GroupName = "SuperAdmin";

    private readonly IMemberManager _memberManager;

    public SuperAdminService(IMemberManager memberManager)
    {
        _memberManager = memberManager;
    }

    public async Task<bool> IsCurrentMemberSuperAdminAsync()
        => await IsSuperAdminAsync(await _memberManager.GetCurrentMemberAsync());

    // [CHANGE: review fixes — overload avoids re-fetching an already loaded member;
    // case-insensitive compare matches the composer's collation-insensitive GetByNameAsync]
    // Related: Web/Controllers/BbvTicketSalesController.cs, Web/Controllers/TicketSalesApiController.cs, Web/Views/Partials/_Navigation.cshtml
    public async Task<bool> IsSuperAdminAsync(MemberIdentityUser? member)
    {
        if (member == null) return false;

        var roles = await _memberManager.GetRolesAsync(member);
        return roles.Contains(GroupName, StringComparer.OrdinalIgnoreCase);
    }
}

namespace TaskFlow.Application.Common.Interfaces;

// Department-scoped authorization. Company admins (role CompanyAdmin/SuperAdmin) are unrestricted;
// everyone else is limited to the departments they administer.
public interface IDepartmentAccess
{
    bool IsCompanyAdmin { get; }

    // Department ids the current user administers (via DepartmentAdmins). Empty for a company admin.
    Task<IReadOnlyList<long>> MyDepartmentIdsAsync(CancellationToken ct = default);

    // CodePrefixes of the departments the current user administers (for filtering the Paymo catalog).
    Task<IReadOnlyList<string>> MyPrefixesAsync(CancellationToken ct = default);

    // May the caller manage this project? Company admin OR admin of the project's department.
    Task<bool> CanManageProjectAsync(long projectId, CancellationToken ct = default);
}

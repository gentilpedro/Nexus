using System.Text.RegularExpressions;

namespace Nexus.Web.Tests;

/// <summary>
/// Structural regression tests for the H6 residual — the workspace/list administration pages
/// authorized once in OnParametersSetAsync and then performed every privileged write without
/// re-checking.
/// </summary>
/// <remarks>
/// <para>
/// These pages carry the most damaging operations in the product: changing a member's role,
/// removing a member, archiving a workspace, deleting labels/custom fields/statuses, and
/// rewriting the workflow rules that gate every status change in a list. A Blazor Server circuit
/// stays alive for as long as the browser tab is open, so the load-time decision can be arbitrarily
/// stale — an admin demoted an hour ago kept all of it until they happened to navigate.
/// </para>
/// <para>
/// Asserted against the source text rather than by rendering the components: these are .razor
/// pages with heavy DI and cascading auth state, and what needs guarding is a property of every
/// handler ("no privileged handler may skip the check"). Reading the source catches a newly added
/// handler that forgets the guard, which a per-handler behavioural test would not.
/// </para>
/// </remarks>
public class AdminPageRevalidationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nexus.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadPage(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nexus.Web", "Components", "Pages", relativePath));

    public static TheoryData<string> AdminPages =>
    [
        Path.Combine("Lists", "LabelsManage.razor"),
        Path.Combine("Lists", "CustomFieldsManage.razor"),
        Path.Combine("Lists", "StatusWorkflowManage.razor"),
        Path.Combine("Workspaces", "WorkspaceSettings.razor"),
    ];

    /// <summary>
    /// Every mutation handler on these pages must re-check authorization before doing anything.
    /// </summary>
    [Theory]
    [MemberData(nameof(AdminPages))]
    public void EveryMutationHandler_RevalidatesBeforeWriting(string page)
    {
        var source = ReadPage(page);

        // Each handler, with the body that follows it up to the next member declaration.
        var handlers = Regex.Matches(
            source,
            @"private\s+async\s+Task\s+(?<name>Handle\w*)\s*\([^)]*\)\s*\{(?<body>.*?)(?=\n    private |\n    public |\n\}\s*$)",
            RegexOptions.Singleline);

        Assert.NotEmpty(handlers);

        var unguarded = handlers
            .Where(m => !m.Groups["body"].Value.Contains("StillAdminAsync()", StringComparison.Ordinal))
            .Select(m => m.Groups["name"].Value)
            .ToList();

        Assert.True(unguarded.Count == 0, $"{page}: handlers without a re-check: {string.Join(", ", unguarded)}");
    }

    /// <summary>
    /// The guard has to run first. A check placed after the delete has already happened protects
    /// nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AdminPages))]
    public void RevalidationHappensBeforeAnyDatabaseWork(string page)
    {
        var source = ReadPage(page);

        var handlers = Regex.Matches(
            source,
            @"private\s+async\s+Task\s+(?<name>Handle\w*)\s*\([^)]*\)\s*\{(?<body>.*?)(?=\n    private |\n    public |\n\}\s*$)",
            RegexOptions.Singleline);

        foreach (Match handler in handlers)
        {
            var body = handler.Groups["body"].Value;
            var guardIndex = body.IndexOf("StillAdminAsync()", StringComparison.Ordinal);

            var firstWrite = new[] { "ExecuteDeleteAsync", "ExecuteUpdateAsync", "SaveChangesAsync", "CreateDbContextAsync" }
                .Select(token => body.IndexOf(token, StringComparison.Ordinal))
                .Where(i => i >= 0)
                .DefaultIfEmpty(-1)
                .Min();

            if (firstWrite < 0)
            {
                continue; // handler touches no database work
            }

            Assert.True(
                guardIndex >= 0 && guardIndex < firstWrite,
                $"{page}: {handler.Groups["name"].Value} performs database work before re-checking authorization.");
        }
    }

    /// <summary>
    /// The helper must ask for Admin specifically. Downgrading it to Member would silently hand
    /// every workspace member the ability to delete statuses and custom fields.
    /// </summary>
    [Theory]
    [MemberData(nameof(AdminPages))]
    public void RevalidationRequiresTheAdminRole(string page)
    {
        var source = ReadPage(page);

        var helper = Regex.Match(
            source,
            @"private\s+async\s+Task<bool>\s+StillAdminAsync\(\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(helper.Success, $"{page}: no StillAdminAsync helper found");
        Assert.Contains("WorkspaceRole.Admin", helper.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("AccessGuard.HasAccessAsync", helper.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// WorkspaceMembers already re-checked on the two destructive paths but not when adding a
    /// member — which is how a demoted admin could still pull people into the workspace.
    /// </summary>
    [Fact]
    public void WorkspaceMembers_RevalidatesOnEveryPrivilegedPath()
    {
        var source = ReadPage(Path.Combine("Workspaces", "WorkspaceMembers.razor"));

        foreach (var handler in new[] { "HandleAddMember", "HandleRoleChange", "HandleRemove" })
        {
            var match = Regex.Match(
                source,
                $@"private\s+async\s+Task\s+{handler}\s*\([^)]*\)\s*\{{(?<body>.*?)(?=\n    private |\n    public )",
                RegexOptions.Singleline);

            Assert.True(match.Success, $"handler {handler} not found");

            var body = match.Groups["body"].Value;
            Assert.True(
                body.Contains("StillAdminAsync()", StringComparison.Ordinal)
                || body.Contains("RevalidateCanManageAsync()", StringComparison.Ordinal),
                $"{handler} does not re-check authorization before writing.");
        }
    }
}

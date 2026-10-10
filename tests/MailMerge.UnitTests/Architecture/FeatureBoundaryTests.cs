using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Shared;
using NetArchTest.Rules;

namespace MailMerge.UnitTests.Architecture;

/// <summary>Feature contracts may be shared deliberately; HTTP slices remain owned by their feature.</summary>
public sealed class FeatureBoundaryTests
{
    private const string Prefix = "MailMerge.Api.Features.";
    private static readonly Types Api = Types.InAssembly(typeof(Campaign).Assembly);
    private static readonly Type[] FeatureTypes = typeof(Campaign).Assembly.GetTypes()
        .Where(t => !t.IsNested && t.Namespace?.StartsWith(Prefix, StringComparison.Ordinal) == true).ToArray();

    // Exact types, not whole namespaces: expanding a feature's public surface requires an explicit decision.
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["Accounts"] = ["Campaigns.Campaign", "Campaigns.CampaignStatus", "Workspaces.Workspace"],
        ["Campaigns"] =
        [
            "Accounts.SendingAccount", "Accounts.AccountKind", "Accounts.Sending.IMailSenderFactory",
            "Accounts.Sending.IMailSender", "Accounts.Sending.OutgoingMessage", "Accounts.Sending.OutgoingAttachment",
            "Accounts.Sending.AccountProblemException", "Accounts.Sending.MessageRejectedException",
            "Accounts.Sending.DeliveryUncertainException", "Contacts.Contact", "Contacts.ContactData",
            "Lists.ContactList", "OptOuts.OptOutRegistry", "OptOuts.OptOutMatcher", "OptOuts.Unsubscribe.UnsubscribeLinks",
            "Workspaces.Workspace",
        ],
        ["Contacts"] =
        [
            "Lists.ContactList", "Lists.ListErrors", "OptOuts.OptOutRegistry", "OptOuts.OptOutMatcher",
            "Campaigns.Message", "Campaigns.MessageStatus",
        ],
        ["Imports"] = ["Contacts.Contact", "Contacts.ContactData", "Lists.ContactList", "Lists.ListErrors"],
        ["Lists"] = ["Contacts.Contact"],
        ["OptOuts"] = ["Workspaces.Workspace"],
        ["Privacy"] =
        [
            "Accounts.SendingAccount", "Campaigns.Campaign", "Campaigns.CampaignStatus", "Campaigns.Message",
            "Campaigns.MessageStatus", "Campaigns.Attachment", "Contacts.Contact", "Lists.ContactList",
            "Imports.ImportUpload", "OptOuts.OptOut", "OptOuts.OptOutRegistry", "Campaigns.CampaignDetails",
            "Campaigns.CampaignErrors", "Accounts.PendingMicrosoftSignIn", "Workspaces.Workspace",
        ],
        ["Workspaces"] = ["Accounts.SendingAccount", "Accounts.AccountKind", "Contacts.Contact", "Campaigns.Campaign"],
    };

    // Models and transport contracts may reference each other; implementation dependencies must be acyclic.
    private static readonly HashSet<string> DataContracts =
    [
        "Accounts.SendingAccount", "Accounts.AccountKind", "Accounts.PendingMicrosoftSignIn",
        "Accounts.Sending.OutgoingMessage", "Accounts.Sending.OutgoingAttachment",
        "Accounts.Sending.AccountProblemException", "Accounts.Sending.MessageRejectedException",
        "Accounts.Sending.DeliveryUncertainException", "Campaigns.Campaign", "Campaigns.CampaignStatus",
        "Campaigns.Message", "Campaigns.MessageStatus", "Campaigns.Attachment", "Contacts.Contact",
        "Lists.ContactList", "Lists.ListErrors", "Campaigns.CampaignDetails", "Campaigns.CampaignErrors", "Imports.ImportUpload", "OptOuts.OptOut",
        "Privacy.PrivacyLogEntry", "Workspaces.Workspace",
    ];

    public static IEnumerable<object[]> Features => FeatureTypes.Select(Feature).Distinct().Order().Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Features))]
    public void Features_do_not_reference_another_features_http_slices(string feature)
    {
        var forbidden = FeatureTypes.Where(t => Feature(t) != feature && IsHttpSlice(t)).Select(t => t.FullName!).ToArray();
        AssertNoDependencies(feature, forbidden);
    }

    [Theory]
    [MemberData(nameof(Features))]
    public void Cross_feature_dependencies_use_explicitly_allowed_contracts(string feature)
    {
        var allowed = AllowedReferences.GetValueOrDefault(feature, []).Select(n => Prefix + n).ToHashSet();
        var forbidden = FeatureTypes.Where(t => Feature(t) != feature && !allowed.Contains(t.FullName!))
            .Select(t => t.FullName!).ToArray();
        AssertNoDependencies(feature, forbidden);
    }

    [Fact]
    public void Shared_code_does_not_depend_on_features_except_persistence_and_workspace_resolution()
    {
        var result = Api.That().ResideInNamespace("MailMerge.Api.Shared")
            .And().DoNotHaveName("MailMergeDbContext", "WorkspaceFilter", "WorkspaceDirectory")
            .ShouldNot().HaveDependencyOn(Prefix.TrimEnd('.')).GetResult();
        AssertSuccess(result);

        // These exceptions may reference only their models, never handlers or feature services.
        var models = new[]
        {
            "Accounts.SendingAccount", "Accounts.PendingMicrosoftSignIn", "Campaigns.Campaign", "Campaigns.Attachment",
            "Campaigns.Message", "Contacts.Contact", "Lists.ContactList", "Imports.ImportUpload", "OptOuts.OptOut",
            "Privacy.PrivacyLogEntry", "Workspaces.Workspace",
        }.Select(n => Prefix + n).ToHashSet();
        AssertSuccess(Api.That().HaveName("MailMergeDbContext").ShouldNot()
            .HaveDependencyOnAny(FeatureTypes.Where(t => !models.Contains(t.FullName!)).Select(t => t.FullName!).ToArray())
            .GetResult());
        AssertSuccess(Api.That().HaveName("WorkspaceFilter", "WorkspaceDirectory").ShouldNot()
            .HaveDependencyOnAny(FeatureTypes.Where(t => t.FullName != Prefix + "Workspaces.Workspace").Select(t => t.FullName!).ToArray())
            .GetResult());
    }

    [Fact]
    public void Feature_implementation_dependencies_do_not_form_cycles()
    {
        var names = Features.Select(f => (string)f[0]).ToArray();
        var graph = names.ToDictionary(n => n, _ => new List<string>());
        foreach (var source in names)
        {
            foreach (var target in names.Where(n => n != source))
            {
                var implementations = FeatureTypes.Where(t => Feature(t) == target
                                                              && !DataContracts.Contains(t.FullName![Prefix.Length..]))
                    .Select(t => t.FullName!).ToArray();
                if (implementations.Length > 0 && !Api.That().ResideInNamespace(Prefix + source)
                        .ShouldNot().HaveDependencyOnAny(implementations).GetResult().IsSuccessful)
                {
                    graph[source].Add(target);
                }
            }
        }

        foreach (var name in names)
        {
            AssertNoCycle(name, graph, [], []);
        }
    }

    private static void AssertNoCycle(string current, Dictionary<string, List<string>> graph,
        HashSet<string> visited, List<string> path)
    {
        path.Should().NotContain(current, $"feature implementations must not form a cycle: {string.Join(" → ", path.Append(current))}");
        if (!visited.Add(current)) return;
        path.Add(current);
        foreach (var next in graph[current]) AssertNoCycle(next, graph, visited, path);
        path.RemoveAt(path.Count - 1);
    }

    private static void AssertNoDependencies(string feature, string[] forbidden)
    {
        var result = Api.That().ResideInNamespace(Prefix + feature).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        if (result.IsSuccessful) return;
        var violations = forbidden.Select(target => (Target: target,
                Result: Api.That().ResideInNamespace(Prefix + feature).ShouldNot().HaveDependencyOn(target).GetResult()))
            .Where(r => !r.Result.IsSuccessful)
            .SelectMany(r => r.Result.FailingTypeNames.Select(source => $"{source} → {r.Target}"));
        result.IsSuccessful.Should().BeTrue($"forbidden references: {string.Join(", ", violations)}");
    }

    private static void AssertSuccess(TestResult result) => result.IsSuccessful.Should().BeTrue(
        $"feature boundary violations: {string.Join(", ", result.FailingTypeNames ?? [])}");

    private static string Feature(Type type) => type.Namespace![Prefix.Length..].Split('.')[0];

    private static bool IsHttpSlice(Type type) => typeof(IHandler).IsAssignableFrom(type)
        || type.Name.EndsWith("Handler", StringComparison.Ordinal)
        || type.Name.EndsWith("Endpoint", StringComparison.Ordinal) || type.Name.EndsWith("Endpoints", StringComparison.Ordinal)
        || type.Name.EndsWith("Request", StringComparison.Ordinal) || type.Name.EndsWith("Response", StringComparison.Ordinal)
        || type.Name.EndsWith("RequestValidator", StringComparison.Ordinal);
}

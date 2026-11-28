using System.Text.RegularExpressions;

namespace GD.SaveEditor.Tests.Build;

/// <summary>
/// The release workflow must run every test before it builds anything: each job other than <c>test</c> has to depend on
/// it, directly or through another job, so a failing test stops the build (manual runs included).
/// </summary>
public sealed partial class ReleaseWorkflowTests
{
    private static readonly Lazy<string> Workflow = new(() => File.ReadAllText(FindWorkflow()));

    [Fact]
    public void Workflow_CanBeStartedManually()
    {
        Assert.Matches(@"(?m)^\s+workflow_dispatch:", Workflow.Value);
    }

    [Fact]
    public void TestJob_RunsTheTestProject()
    {
        var jobs = ParseJobs(Workflow.Value);

        Assert.True(jobs.TryGetValue("test", out var test), "The workflow has no 'test' job.");
        Assert.Contains("dotnet test", test.Body);
        Assert.Contains("TEST_PROJECT", test.Body);
        Assert.Matches(@"(?m)^\s+TEST_PROJECT:\s*GD\.SaveEditor/GD\.SaveEditor\.Tests/GD\.SaveEditor\.Tests\.csproj\s*$", Workflow.Value);
        Assert.Empty(test.Needs);
    }

    [Fact]
    public void EveryOtherJob_WaitsForTheTests()
    {
        var jobs = ParseJobs(Workflow.Value);

        Assert.Contains("version", jobs.Keys);
        Assert.Contains("release", jobs.Keys);
        foreach (var name in jobs.Keys.Where(n => n != "test"))
            Assert.True(DependsOn(jobs, name, "test", []), $"Job '{name}' can start before the tests passed.");
    }

    [Fact]
    public void TestProjectFile_ExistsAtThePathTheWorkflowUses()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(FindWorkflow())))!;

        Assert.True(File.Exists(Path.Combine(root, "GD.SaveEditor", "GD.SaveEditor.Tests", "GD.SaveEditor.Tests.csproj")));
    }

    private static bool DependsOn(IReadOnlyDictionary<string, Job> jobs, string job, string target, HashSet<string> seen)
    {
        if (!seen.Add(job) || !jobs.TryGetValue(job, out var info))
            return false;
        return info.Needs.Contains(target) || info.Needs.Any(n => DependsOn(jobs, n, target, seen));
    }

    /// <summary>The jobs under <c>jobs:</c> (indented by two spaces), with their body and <c>needs</c>.</summary>
    private static Dictionary<string, Job> ParseJobs(string yaml)
    {
        var jobsSection = yaml[(yaml.IndexOf("\njobs:", StringComparison.Ordinal) + 1)..];
        var matches = JobHeader().Matches(jobsSection);
        var jobs = new Dictionary<string, Job>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : jobsSection.Length;
            var body = jobsSection[start..end];
            jobs[matches[i].Groups[1].Value] = new Job(body, ParseNeeds(body));
        }

        return jobs;
    }

    private static IReadOnlyList<string> ParseNeeds(string body)
    {
        var match = Needs().Match(body);
        if (!match.Success)
            return [];
        return match.Groups[1].Value.Trim().Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string FindWorkflow()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".github", "workflows", "release-installer.yml");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("The release workflow (.github/workflows/release-installer.yml) was not found above the test folder.");
    }

    private sealed record Job(string Body, IReadOnlyList<string> Needs);

    [GeneratedRegex(@"(?m)^  ([A-Za-z_][\w-]*):\s*$")]
    private static partial Regex JobHeader();

    [GeneratedRegex(@"(?m)^    needs:\s*(.+)$")]
    private static partial Regex Needs();
}

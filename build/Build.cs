using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.DocFX.DocFXTasks;
using static Nuke.Common.IO.TextTasks;
using Nuke.Common.Tools.DocFX;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(NoFetch = true)] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath DocsDirectory => RootDirectory / "docs";
    AbsolutePath DocFxFile => RootDirectory / "docs" / "docfx.json";
    AbsolutePath ChangelogFile => RootDirectory / "CHANGELOG.md";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .DependsOn(GenerateVersion)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore());
        });

    Target GenerateVersion => _ => _
    .Executes(() =>
    {
        var buildDate = DateTime.UtcNow;

        var filePath = SourceDirectory / "server" / "Dangl.OpenCDE.Shared" / "VersionsService.cs";

        var currentDateUtc = $"new DateTime({buildDate.Year}, {buildDate.Month}, {buildDate.Day}, {buildDate.Hour}, {buildDate.Minute}, {buildDate.Second}, DateTimeKind.Utc)";

        var content = $@"using System;

namespace Dangl.OpenCDE.Shared
{{
    // This file is automatically generated
    [System.CodeDom.Compiler.GeneratedCode(""GitVersionBuild"", """")]
    public static class VersionsService
    {{
        public static string Version => ""{GitVersion.NuGetVersionV2}"";
        public static string CommitInfo => ""{GitVersion.FullBuildMetaData}"";
        public static string CommitDate => ""{GitVersion.CommitDate}"";
        public static string CommitHash => ""{GitVersion.Sha}"";
        public static string InformationalVersion => ""{GitVersion.InformationalVersion}"";
        public static DateTime BuildDateUtc {{ get; }} = {currentDateUtc};
    }}
}}";
        WriteAllText(filePath, content);
    });

    Target BuildDocFxMetadata => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DocFXMetadata(x => x
                .SetProcessEnvironmentVariable("DOCFX_SOURCE_BRANCH_NAME", GitVersion.BranchName)
                .SetProjects(DocFxFile));
        });

    Target BuildDocumentation => _ => _
        .DependsOn(Clean)
        .DependsOn(BuildDocFxMetadata)
        .Executes(() =>
        {
            CopyFile(ChangelogFile, DocsDirectory / "CHANGELOG.md");
            DocFXBuild(x => x
                .SetProcessEnvironmentVariable("DOCFX_SOURCE_BRANCH_NAME", GitVersion.BranchName)
                .SetConfigFile(DocFxFile));
            DeleteFile(DocsDirectory / "CHANGELOG.md");
        });

}

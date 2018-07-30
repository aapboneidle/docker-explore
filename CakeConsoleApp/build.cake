#addin "nuget:?package=Cake.Incubator"
#addin "nuget:?package=Cake.Hg"
#addin "nuget:?package=SemanticVersioning"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

readonly string target = Argument("target", "Default");
readonly string configuration = Argument("configuration", "Release");
// The SEA versioning guidance 
// (https://sharepoint.sea.co.uk/sites/bms17/Shared%20Documents/engineering/software%20engineering/implementation/resources/N0145%20software%20version%20numbering%20scheme.pdf)
// currently states that pre-release versions are not used.
// This option overrides the guidance.
readonly bool buildPreReleaseAllowed = HasArgument("allow-pre-release-build");

//////////////////////////////////////////////////////////////////////
// ENVIRONMENT
//////////////////////////////////////////////////////////////////////
readonly string localNugetFolder = EnvironmentVariable<string>("BUILD_LOCAL_NUGET_FOLDER", @"C:\MyNuget");

//////////////////////////////////////////////////////////////////////
// CONFIGURATION
//////////////////////////////////////////////////////////////////////
const string nupkgFiles = "**/bin/**/*.nupkg";

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Solution file
var solution = GetFiles("*.sln").FirstOrDefault();

// NuGet
// TODO: need to cope with apps as well
var csproj = GetFiles("**/*.csproj").FirstOrDefault();

// Jenkins
var isJenkinsBuild = Jenkins.IsRunningOnJenkins;

// Hg
var hgRepo = Hg("."); // *** Fails on Jenkins if the job name contains spaces
var hgBranch = hgRepo.Branch();
var isFeatureBranch = hgBranch.StartsWith("feature/");
var isDevelopBranch = hgBranch.Equals("develop");
var isReleaseBranch = hgBranch.StartsWith("release/");
var isHotfixBranch = hgBranch.StartsWith("hotfix/");
var isDefaultBranch = hgBranch.Equals("default");

// Semver version numbers set by Versioning task
string versionPrefix; // major.minor.patch
string versionSuffix; // If empty string, nupkg is marked as pre-release.
string assemblyVersion; // major.minor.patch.build

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

// CLEAN
// Invokes msbulld clean, and removes nupkg files from build tree
Task("Clean")
  .Does(() =>
  {
    if(IsRunningOnWindows())
    {
      // Use MSBuild
      MSBuild(solution, settings =>
        settings.SetConfiguration(configuration)
          .WithTarget("Clean"));
    }
    else
    {
      // Use XBuild
      XBuild(solution, settings =>
        settings.SetConfiguration(configuration)
          .WithTarget("Clean"));
    }
  })
  .DoesForEach(GetFiles(nupkgFiles), f => DeleteFile(f));

// DEEP CLEAN
// Deletes nupkg files
// Leaves cache files in obj intact
Task("Deep-Clean")
  .Description("Cleans all bin and obj sub-folders")
  .Does(() => 
  {
    CleanDirectories(GetDirectories("**/obj/*"));
    CleanDirectories(GetDirectories("**/bin/*"));
  })
  .DoesForEach(GetFiles(nupkgFiles), f => DeleteFile(f));

// VERSIONING
// This task must set versionPrefix, versionSuffix and assemblyVersion

Task("Versioning")
  .Description("Determine version number to build")
  .Does(() =>
  { 
    // .NET Core projects have version number included as a csproj property
    versionPrefix = XmlPeek(csproj, "/Project/PropertyGroup/Version"); 

    if (isDefaultBranch) // production build
    {
      VerifyVersion(versionPrefix, LatestVersionOnBranch(hgBranch));
      versionSuffix = isJenkinsBuild ? "" : "-alpha";
    }
    if (isReleaseBranch)
    {
      // use the branch name (after "/") as the version, and mark as release candidate
      VerifyVersion(versionPrefix, hgBranch.Replace("release/",""));
      versionSuffix = isJenkinsBuild ? "-rc" : "-alpha";
      // TODO: consider adding a number to rc (e.g. rc.1 rc.2) depending on what has already been published
    }
    if (isHotfixBranch)
    {
      // use the branch name (after "/") as the version, and mark as release candidate
      VerifyVersion(versionPrefix, hgBranch.Replace("hotfix/",""));
      versionSuffix = isJenkinsBuild ? "-rc" : "-alpha";
      // TODO: consider adding a number to rc (e.g. rc.1 rc.2) depending on what has already been published
    }
    else if (isDevelopBranch)
    {
      versionSuffix = isJenkinsBuild ? "-beta" : "-alpha";
    }
    else // feature branch
    {
      // No version check
      versionSuffix = isJenkinsBuild ? "-alpha" : "-alpha";
    }
    
    // Conform to SEA guidance unless overriden by "-allow-pre-release-build" flag
    if (!buildPreReleaseAllowed){
      versionSuffix = "";
    } 

    var buildNumber = isJenkinsBuild ? Jenkins.Environment.Build.BuildNumber : 0;
    assemblyVersion = versionPrefix + "." + buildNumber;

    Information("prefix: [" + versionPrefix + "] suffix: [" + versionSuffix + "] assembly: [" + assemblyVersion + "]");

    CheckVersions();
  });

// NUGET
// Ensure that nuget sources are configured in cake.config
// TODO: Production builds should not be able to use pre-release components
Task("Restore-NuGet-Packages")
  .Description("Restores nuget dependencies")
  .IsDependentOn("Versioning")
  .Does(() =>
  {
    // Done by MSBuild for .NET Core
  });

// BUILD
// Version number of assembly and nupkg controlled as described 
// here: https://stackoverflow.com/questions/43274254/setting-the-version-number-for-net-core-projects-csproj-not-json-projects
// "Pack" target is needed to workaround nuget pack not working for.NET Standard and .NET Core class libraries
//     (see http://blog.tdwright.co.uk/2017/11/20/the-perils-of-publishing-a-net-standard-library-to-nuget/)
Task("Build")
  .IsDependentOn("Clean")
  .IsDependentOn("Restore-NuGet-Packages")
  .IsDependentOn("Versioning")
  .Does(() =>
  {
    CheckVersions();

    var nodeId = hgRepo.Identify().ToString();

    if(IsRunningOnWindows())
    {
      // Use MSBuild
      MSBuild(solution, settings =>
        settings.SetConfiguration(configuration)
        .WithProperty("AssemblyVersion", assemblyVersion)
        .WithProperty("FileVersion", assemblyVersion)
        .WithProperty("PackageVersion", $"{versionPrefix}{versionSuffix}")
        .WithProperty("InformationalVersion", "Hg: " + nodeId)
        .WithTarget("Pack"));
    }
    else
    {
      // Use XBuild
      XBuild(solution, settings =>
        settings.SetConfiguration(configuration)
        .WithTarget("Pack"));
    }
  });

// UNIT TESTS
// Run all unit tests 
Task("Run-Unit-Tests")
  .IsDependentOn("Build")
  .Does(() =>
  {
    // TODO
      // NUnit3("./src/**/bin/" + configuration + "/*.Tests.dll", new NUnit3Settings {
      //     NoResults = true
      //     });
  });

// PUBLISH
// Publish nuget package or installer to SEA repo
Task("Publish")
  .IsDependentOn("Run-Unit-Tests")
  .WithCriteria(() => isJenkinsBuild)
  .WithCriteria(() => !isFeatureBranch)
  .Does(() =>
  {
    // TODO
  });

// PUBLISH-LOCAL
// Publish nuget package to local repo
Task("Publish-Local")
  .IsDependentOn("Run-Unit-Tests")
  .WithCriteria(() => !isJenkinsBuild)
  .Does(() => CopyFiles(GetFiles(nupkgFiles),localNugetFolder));
  
// SONAR
// Run Sonar analysis in this folder only
Task("Sonar")
  .IsDependentOn("Build")
  .WithCriteria(() => !isFeatureBranch)
  .WithCriteria(() => isJenkinsBuild)
  .Does(() =>
  {
    // TODO
  });

// DEFAULT TASK
// If you don't specify a target, this is the task that it is run
Task("Default")
  .IsDependentOn("Publish-Local")
  .IsDependentOn("Publish")
  .IsDependentOn("Sonar");

RunTarget(target);

// Given a list of version numbers, returns the one that is the latest according to semantic versioning rules
private string LatestVersion(IEnumerable<string> vtags)
{
  return vtags.Select(tag => new SemVer.Version(tag)).OrderByDescending(t => t).FirstOrDefault().ToString();
}

private string LatestVersionOnBranch(string branch)
{
  // Find tags of the form "v1.2.3" (as generated by hg flow) on the current branch
  var versions = hgRepo.Log(RevSpec.InBranch(hgBranch))
    .SelectMany(cs => cs.Tags)
    .Where(t => t.StartsWith("v"))
    .Select(t => t.Replace("v",""));
  // get the latest version
  return LatestVersion(versions);
}

private void CheckVersions()
{
    if ( versionPrefix == null || versionSuffix == null || assemblyVersion == null )
    {
      throw new Exception("One or more elements of version number not set");
    }
}

// Check that version number is correct with respect to branch and tags
private void VerifyVersion(string actualVersion, string expectedVersion)
{
  if( !actualVersion.Equals(expectedVersion) )
  {
    throw new Exception($"Version to be built is {actualVersion} but tag/branch specifies {expectedVersion}");
  }
}
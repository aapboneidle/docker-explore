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
readonly string localNugetFolder = EnvironmentVariable("BUILD_LOCAL_NUGET_FOLDER") ?? @"~/MyNuget";

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
      // Use MSBuild
      DotNetCoreMSBuild(solution, new DotNetCoreMSBuildSettings()
        .SetConfiguration(configuration)
        .WithTarget("Clean"));
  })
  ;

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
  ;

// VERSIONING
// This task must set versionPrefix, versionSuffix and assemblyVersion

Task("Versioning")
  .Description("Determine version number to build")
  .Does(() =>
  { 
    // .NET Core projects have version number included as a csproj property
    versionPrefix = XmlPeek(csproj, "/Project/PropertyGroup/Version"); 
 
    versionSuffix = isJenkinsBuild ? "-alpha" : "-alpha";

    var buildNumber = isJenkinsBuild ? Jenkins.Environment.Build.BuildNumber : 0;
    assemblyVersion = versionPrefix + "." + buildNumber;

    Information($"csproj: [{csproj}] prefix: [{versionPrefix}] suffix: [{versionSuffix}] assembly: [{assemblyVersion}]");

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
      MSBuild(solution, settings =>
        settings.SetConfiguration(configuration)
        .WithProperty("AssemblyVersion", assemblyVersion)
        .WithProperty("FileVersion", assemblyVersion)
        .WithProperty("PackageVersion", $"{versionPrefix}{versionSuffix}")
        .WithTarget("Pack"));
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

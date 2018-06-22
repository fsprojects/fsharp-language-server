using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Logging;
using Microsoft.Build.Framework;
using System.Reflection;

namespace FSharpLanguageServer {
    class ProjectInfo {
        static void Main(string[] args) {
            var basePath = "/usr/local/share/dotnet/sdk/2.1.300";
            Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(basePath, "Sdks"));
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(basePath, "MSBuild.dll"));
            var globalProperties = new Dictionary<string, string>();
            globalProperties.Add("DesignTimeBuild", "true");
            globalProperties.Add("BuildingInsideVisualStudio", "true");
            globalProperties.Add("BuildProjectReferences", "false");
            globalProperties.Add("_ResolveReferenceDependencies", "true");
            globalProperties.Add("SolutionDir", "/Users/georgefraser/Documents/fsharp-language-server/sample");
            // Setting this property will cause any XAML markup compiler tasks to run in the
            // current AppDomain, rather than creating a new one. This is important because
            // our AppDomain.AssemblyResolve handler for MSBuild will not be connected to
            // the XAML markup compiler's AppDomain, causing the task not to be able to find
            // MSBuild.
            globalProperties.Add("AlwaysCompileMarkupFilesInSeparateDomain", "false");
            // This properties allow the design-time build to handle the Compile target without actually invoking the compiler.
            // See https://github.com/dotnet/roslyn/pull/4604 for details.
            globalProperties.Add("ProvideCommandLineArgs", "true");
            globalProperties.Add("SkipCompilerExecution", "true" );
            var projectCollection = new ProjectCollection(globalProperties);
            var project = projectCollection.LoadProject("/Users/georgefraser/Documents/fsharp-language-server/sample/ReferenceCSharp/ReferenceCSharp.fsproj");
            var projectInstance = project.CreateProjectInstance();
            var buildResult = projectInstance.Build(new string[] { "Compile", "CoreCompile" }, new ILogger[]{ new ConsoleLogger() });
            String type = "";
            foreach (var item in projectInstance.Items) {
                if (type != item.ItemType) {
                    type = item.ItemType;
                    Console.WriteLine(type);
                }
                Console.WriteLine("  " + item.EvaluatedInclude);
            }
        }
    }
}
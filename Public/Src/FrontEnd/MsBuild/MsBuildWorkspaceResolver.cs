// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json;
using TypeScript.Net.DScript;
using static BuildXL.Utilities.FormattableStringEx;
using ISourceFile = TypeScript.Net.Types.ISourceFile;
using SourceFile = TypeScript.Net.Types.SourceFile;

namespace BuildXL.FrontEnd.MsBuild
{

    /// <summary>
    /// Workspace resolver using the MsBuild static graph API
    /// </summary>
    public class MsBuildWorkspaceResolver : DScriptInterpreterBase, Sdk.Workspaces.IDScriptWorkspaceModuleResolver, Workspaces.Core.IWorkspaceModuleResolver
    {
        internal const string MsBuildResolverName = "MsBuild";

        private IMsBuildResolverSettings m_resolverSettings;

        // path-to-source-file to source file. Parsing requests may happen concurrently.
        private readonly ConcurrentDictionary<AbsolutePath, SourceFile> m_createdSourceFiles =
            new ConcurrentDictionary<AbsolutePath, SourceFile>();

        private Possible<ProjectGraphResult>? m_projectGraph;

        /// <summary>
        /// Set of well known locations that are used to identify a candidate entry point to parse, if a specific one is not provided
        /// </summary>
        private static HashSet<string> s_wellKnownEntryPointExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase){"proj", "sln"};

        /// <summary>
        /// Collection of environment variables that are allowed to the graph construction process to see (in addition to the ones specified by the user)
        /// </summary>
        private static string[] s_environmentVariableWhitelist = new[] 
            {
                "ComSpec",
                "PATH",
                "PATHEXT",
                "NUMBER_OF_PROCESSORS",
                "OS",
                "PROCESSOR_ARCHITECTURE",
                "PROCESSOR_IDENTIFIER",
                "PROCESSOR_LEVEL",
                "PROCESSOR_REVISION",
                "SystemDrive",
                "SystemRoot",
                "SYSTEMTYPE"
            };
        private QualifierId[] m_requestedQualifiers;

        /// <summary>
        /// Keep in sync with the BuildXL deployment spec that places the tool
        /// </summary>
        private RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\MsBuildGraphBuilder\ProjectGraphBuilder.exe");

        /// <summary>
        /// The result of computing the build graph
        /// </summary>
        public Possible<ProjectGraphResult> ComputedProjectGraph
        {
            get
            {
                Contract.Assert(m_projectGraph.HasValue, "The computation of the build graph should have been triggered to be able to retrieve this value");
                return m_projectGraph.Value;
            }
        } 

        /// <inheritdoc/>
        public MsBuildWorkspaceResolver(
            IFrontEndStatistics statistics)
            : base(statistics, logger: null)
        {
            Name = nameof(MsBuildWorkspaceResolver);
        }

        /// <inheritdoc cref="DScriptInterpreterBase"/>
        public override Task<Possible<ISourceFile>> TryParseAsync(
            AbsolutePath pathToParse,
            AbsolutePath moduleOrConfigPathPromptingParse,
            ParsingOptions parsingOptions = null)
        {
            return Task.FromResult(Possible.Create((ISourceFile)GetOrCreateSourceFile(pathToParse)));
        }

        /// <inheritdoc/>
        public string DescribeExtent()
        {
            Possible<HashSet<ModuleDescriptor>> maybeModules = GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();

            if (!maybeModules.Succeeded)
            {
                return I($"Module extent could not be computed. {maybeModules.Failure.Describe()}");
            }

            return string.Join(", ", maybeModules.Result.Select(module => module.Name));
        }

        /// <inheritdoc/>
        public virtual string Kind => KnownResolverKind.MsBuildResolverKind;

        /// <inheritdoc/>
        public bool TryInitialize(
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IResolverSettings resolverSettings,
            QualifierId[] requestedQualifiers)
        {
            Contract.Requires(requestedQualifiers?.Length > 0);

            InitializeInterpreter(host, context, configuration);

            m_resolverSettings = resolverSettings as IMsBuildResolverSettings;
            Contract.Assert(m_resolverSettings != null);

            m_requestedQualifiers = requestedQualifiers;

            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<HashSet<ModuleDescriptor>>> GetAllKnownModuleDescriptorsAsync()
        {
            var result = (await TryComputeBuildGraphIfNeededAsync())
                .Then(projectGraphResult => new HashSet<ModuleDescriptor> { projectGraphResult.ModuleDefinition.Descriptor });

            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDefinition>> TryGetModuleDefinitionAsync(ModuleDescriptor moduleDescriptor)
        {
            Possible<ModuleDefinition> result = (await TryComputeBuildGraphIfNeededAsync()).Then<ModuleDefinition>(
                parsedResult =>
                {
                    // There is a single module, so we check against that
                    if (parsedResult.ModuleDefinition.Descriptor != moduleDescriptor)
                    {
                        return new ModuleNotOwnedByThisResolver(moduleDescriptor);
                    }

                    return parsedResult.ModuleDefinition;
                });

            return result;
        }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public async ValueTask<Possible<IReadOnlyCollection<ModuleDescriptor>>> TryGetModuleDescriptorsAsync(ModuleReferenceWithProvenance moduleReference)
        {
            Possible<IReadOnlyCollection<ModuleDescriptor>> result = (await TryComputeBuildGraphIfNeededAsync()).Then(
                parsedResult =>
                    (IReadOnlyCollection<ModuleDescriptor>)(
                        parsedResult.ModuleDefinition.Descriptor.Name == moduleReference.Name ?
                            new[] { parsedResult.ModuleDefinition.Descriptor }
                            : CollectionUtilities.EmptyArray<ModuleDescriptor>()));

            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<Possible<ModuleDescriptor>> TryGetOwningModuleDescriptorAsync(AbsolutePath specPath)
        {
            Possible<ModuleDescriptor> result = (await TryComputeBuildGraphIfNeededAsync()).Then<ModuleDescriptor>(
                parsedResult =>
                {
                    if (!parsedResult.ModuleDefinition.Specs.Contains(specPath))
                    {
                        return new SpecNotOwnedByResolverFailure(specPath.ToString(Context.PathTable));
                    }

                    return parsedResult.ModuleDefinition.Descriptor;
                });

            return result;
        }

        /// <inheritdoc/>
        public Task ReinitializeResolver() => Task.FromResult<object>(null);

        /// <inheritdoc />
        public ISourceFile[] GetAllModuleConfigurationFiles()
        {
            return CollectionUtilities.EmptyArray<ISourceFile>();
        }

        private SourceFile GetOrCreateSourceFile(AbsolutePath path)
        {
            Contract.Assert(path.IsValid);

            if (m_createdSourceFiles.TryGetValue(path, out SourceFile sourceFile))
            {
                return sourceFile;
            }

            // This is the interop point for MSBuild to advertise values to other DScript specs
            // For now we just return an empty SourceFile

            // TODO: Add the qualifier space to the (empty now) generated ISourceFile for future interop
            sourceFile = SourceFile.Create(path.ToString(Context.PathTable));
            m_createdSourceFiles.Add(path, sourceFile);

            return sourceFile;
        }

        private async Task<Possible<ProjectGraphResult>> TryComputeBuildGraphIfNeededAsync()
        {
            if (m_projectGraph == null)
            {
                // Get the locations where the MsBuild assemblies should be searched
                if (!TryRetrieveMsBuildSearchLocations(out IEnumerable<AbsolutePath> searchLocations))
                {
                    // Errors should have been logged
                    return new MsBuildGraphConstructionFailure(m_resolverSettings, Context.PathTable);
                }

                if (!TryRetrieveParsingEntryPoint(out IEnumerable<AbsolutePath> parsingEntryPoints))
                {
                    // Errors should have been logged
                    return new MsBuildGraphConstructionFailure(m_resolverSettings, Context.PathTable);
                }

                BuildParameters.IBuildParameters buildParameters = RetrieveBuildParameters();

                m_projectGraph = await TryComputeBuildGraphAsync(searchLocations, parsingEntryPoints, buildParameters);
            }

            return m_projectGraph.Value;
        }

        private BuildParameters.IBuildParameters RetrieveBuildParameters()
        {
            // If the environment is not specified, expose the whole process environment
            if (m_resolverSettings.Environment == null)
            {
                return BuildParameters.GetFactory().PopulateFromEnvironment();
            }

            // Otherwise, just what the environment specified (plus a handful of extra required ones)

            // User-configured environment
            var configuredEnvironment = BuildParameters.GetFactory().PopulateFromDictionary(m_resolverSettings.Environment);

            // Combine the ones above with a set of OS-wide properties processes should see
            var buildParameters = BuildParameters
                .GetFactory()
                .PopulateFromEnvironment()
                .Select(s_environmentVariableWhitelist)
                .Override(configuredEnvironment.ToDictionary());

            return buildParameters;
        }

        private bool TryRetrieveParsingEntryPoint(out IEnumerable<AbsolutePath> parsingEntryPoints)
        {
            if (m_resolverSettings.FileNameEntryPoints?.Count > 0)
            {
                parsingEntryPoints = m_resolverSettings.FileNameEntryPoints.Select(entryPoint => m_resolverSettings.RootTraversal.Combine(Context.PathTable, entryPoint));
                return true;
            }

            // Retrieve all files directly under the root traversal whose extensions end with any of the well known entry point extensions
            List<AbsolutePath> filesInRootTraversal = Engine
                .EnumerateFiles(m_resolverSettings.RootTraversal, recursive: false)
                .Where(file => s_wellKnownEntryPointExtensions
                                    .Any(extension => file
                                        .GetName(Context.PathTable)
                                        .GetExtension(Context.StringTable)
                                        .ToString(Context.StringTable)
                                        .EndsWith(extension, StringComparison.OrdinalIgnoreCase))).ToList();

            // If there is a single element, that's the one
            if (filesInRootTraversal.Count == 1)
            {
                parsingEntryPoints = filesInRootTraversal;
                return true;
            }

            // Otherwise, we don't really know where to start, and the user should specify that more precisely

            if (filesInRootTraversal.Count == 0)
            {
                Tracing.Logger.Log.CannotFindParsingEntryPoint(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), m_resolverSettings.RootTraversal.ToString(Context.PathTable));
            }
            else
            {
                Tracing.Logger.Log.TooManyParsingEntryPointCandidates(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), m_resolverSettings.RootTraversal.ToString(Context.PathTable));
            }
            
            parsingEntryPoints = null;
            return false;

        }

        private async Task<Possible<ProjectGraphResult>> TryComputeBuildGraphAsync(IEnumerable<AbsolutePath> searchLocations, IEnumerable<AbsolutePath> parsingEntryPoints, BuildParameters.IBuildParameters buildParameters)
        {
            // We create a unique output file on the obj folder associated with the current front end, and using a GUID as the file name
            AbsolutePath outputDirectory = FrontEndHost.GetFolderForFrontEnd(MsBuildFrontEnd.Name);
            AbsolutePath outputFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());
            // We create a unique response file that will contain the tool arguments
            AbsolutePath responseFile = outputDirectory.Combine(Context.PathTable, Guid.NewGuid().ToString());

            // Make sure the directories are there
            FileUtilities.CreateDirectory(outputDirectory.ToString(Context.PathTable));

            Possible<ProjectGraphWithPredictionsResult<AbsolutePath>> maybeProjectGraphResult = await ComputeBuildGraphAsync(responseFile, parsingEntryPoints, outputFile, searchLocations, buildParameters);

            if (!maybeProjectGraphResult.Succeeded)
            {
                // A more specific error has been logged already
                return maybeProjectGraphResult.Failure;
            }

            var projectGraphResult = maybeProjectGraphResult.Result;

            if (m_resolverSettings.KeepProjectGraphFile != true)
            {
                DeleteGraphBuilderRelatedFiles(outputFile, responseFile);
            }
            else
            {
                // Graph-related files are requested to be left on disk. Let's print a message with their location.
                Tracing.Logger.Log.GraphBuilderFilesAreNotRemoved(Context.LoggingContext, outputFile.ToString(Context.PathTable), responseFile.ToString(Context.PathTable));
            }

            if (!projectGraphResult.Succeeded)
            {
                var failure = projectGraphResult.Failure;
                Tracing.Logger.Log.ProjectGraphConstructionError(Context.LoggingContext, failure.HasLocation ? failure.Location : m_resolverSettings.Location(Context.PathTable), failure.Message);

                return new MsBuildGraphConstructionFailure(m_resolverSettings, Context.PathTable);
            }

            ProjectGraphWithPredictions<AbsolutePath> projectGraph = projectGraphResult.Result;

            // The module contains all project files that are part of the graph
            var projectFiles = new HashSet<AbsolutePath>();
            foreach (ProjectWithPredictions<AbsolutePath> node in projectGraph.ProjectNodes)
            {
                projectFiles.Add(node.FullPath);
            }

            var moduleDescriptor = ModuleDescriptor.CreateWithUniqueId(m_resolverSettings.ModuleName, this);
            var moduleDefinition = ModuleDefinition.CreateModuleDefinitionWithImplicitReferences(
                moduleDescriptor,
                m_resolverSettings.RootTraversal,
                m_resolverSettings.File,
                projectFiles,
                allowedModuleDependencies: null, // no module policies
                cyclicalFriendModules: null); // no whitelist of cycles

            return new ProjectGraphResult(projectGraph, moduleDefinition, projectGraphResult.PathToMsBuildExe);
        }

        private void DeleteGraphBuilderRelatedFiles(AbsolutePath outputFile, AbsolutePath responseFile)
        {
            // Remove the file with the serialized graph and the response file, so we leave no garbage behind
            // If there is a problem deleting these file, unlikely to happen (the process that created it should be gone by now), log as a warning and move on, this is not
            // a blocking problem

            try
            {
                FileUtilities.DeleteFile(outputFile.ToString(Context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.CannotDeleteSerializedGraphFile(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), outputFile.ToString(Context.PathTable), ex.Message);
            }

            try
            {
                FileUtilities.DeleteFile(responseFile.ToString(Context.PathTable));
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.CannotDeleteResponseFile(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), responseFile.ToString(Context.PathTable), ex.Message);
            }
        }

        /// <summary>
        /// Retrieves a list of search locations for the required MsBuild assemblies
        /// </summary>
        /// <remarks>
        /// First inspects the resolver configuration to check if these are defined explicitly. Otherwise, uses PATH environment variable.
        /// </remarks>
        private bool TryRetrieveMsBuildSearchLocations(out IEnumerable<AbsolutePath> searchLocations)
        {
            return FrontEndUtilities.TryRetrieveExecutableSearchLocations(
                MsBuildFrontEnd.Name,
                Context,
                Engine,
                m_resolverSettings.MsBuildSearchLocations?.SelectList(directoryLocation => directoryLocation.Path),
                out searchLocations,
                () => Tracing.Logger.Log.NoSearchLocationsSpecified(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable)),
                paths => Tracing.Logger.Log.CannotParseBuildParameterPath(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), paths)
            );
        }

        private async Task<Possible<ProjectGraphWithPredictionsResult<AbsolutePath>>> ComputeBuildGraphAsync(
            AbsolutePath responseFile,
            IEnumerable<AbsolutePath> projectEntryPoints, 
            AbsolutePath outputFile, 
            IEnumerable<AbsolutePath> searchLocations, 
            BuildParameters.IBuildParameters buildParameters)
        {
            SandboxedProcessResult result = await RunMsBuildGraphBuilderAsync(responseFile, projectEntryPoints, outputFile, searchLocations, buildParameters);

            string standardError = result.StandardError.CreateReader().ReadToEndAsync().GetAwaiter().GetResult();

            if (result.ExitCode != 0)
            {
                // In case of a cancellation, the tool may have exited with a non-zero
                // code, but that's expected
                if (!Context.CancellationToken.IsCancellationRequested)
                {
                    // This should never happen! Report the standard error and exit gracefully
                    Tracing.Logger.Log.GraphConstructionInternalError(
                        Context.LoggingContext,
                        m_resolverSettings.Location(Context.PathTable),
                        standardError);
                }

                return new MsBuildGraphConstructionFailure(m_resolverSettings, Context.PathTable);
            }

            // If the tool exited gracefully, but standard error is not empty, that 
            // is interpreted as a warning. We propagate that to the BuildXL log
            if (!string.IsNullOrEmpty(standardError))
            {
                Tracing.Logger.Log.GraphConstructionFinishedSuccessfullyButWithWarnings(
                    Context.LoggingContext,
                    m_resolverSettings.Location(Context.PathTable),
                    standardError);
            }

            TrackFilesAndEnvironment(buildParameters, result.AllUnexpectedFileAccesses, outputFile.GetParent(Context.PathTable));

            var serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);
            serializer.Converters.Add(new AbsolutePathJsonConverter(Context.PathTable));

            using (var sr = new StreamReader(outputFile.ToString(Context.PathTable)))
            using (var reader = new JsonTextReader(sr))
            {
                var projectGraphWithPredictionsResult = serializer.Deserialize<ProjectGraphWithPredictionsResult<AbsolutePath>>(reader);

                // A successfully constructed graph should always have a valid path to MsBuild
                Contract.Assert(!projectGraphWithPredictionsResult.Succeeded || projectGraphWithPredictionsResult.PathToMsBuildExe.IsValid);
                // A successfully constructed graph should always have at least one project node
                Contract.Assert(!projectGraphWithPredictionsResult.Succeeded || projectGraphWithPredictionsResult.Result.ProjectNodes.Length > 0);
                // A failed construction should always have a failure set
                Contract.Assert(projectGraphWithPredictionsResult.Succeeded || projectGraphWithPredictionsResult.Failure != null);

                // Let's log the paths to the used MsBuild assemblies, just for debugging purposes
                Tracing.Logger.Log.GraphConstructionToolCompleted(
                    Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), 
                    string.Join(",\n", projectGraphWithPredictionsResult.MsBuildAssemblyPaths.Select(kvp => I($"[{kvp.Key}]:{kvp.Value.ToString(Context.PathTable)}"))), 
                    projectGraphWithPredictionsResult.PathToMsBuildExe.ToString(Context.PathTable));

                return projectGraphWithPredictionsResult;
            }
        }

        private void TrackFilesAndEnvironment(BuildParameters.IBuildParameters buildParameters, ISet<ReportedFileAccess> fileAccesses, AbsolutePath frontEndFolder)
        {
            // Register all build parameters passed to the graph construction process
            // TODO: we actually need the build parameters *used* by the graph construction process, but for now this is a compromise to keep
            // graph caching sound. We need to modify this when MsBuild static graph API starts providing used env vars.
            foreach (string key in buildParameters.ToDictionary().Keys)
            {
                Engine.TryGetBuildParameter(key, MsBuildFrontEnd.Name, out _);
            }

            FrontEndUtilities.TrackToolFileAccesses(Engine, Context, MsBuildFrontEnd.Name, fileAccesses, frontEndFolder);
        }

        private Task<SandboxedProcessResult> RunMsBuildGraphBuilderAsync(
            AbsolutePath responseFile,
            IEnumerable<AbsolutePath> projectEntryPoints, 
            AbsolutePath outputFile, 
            IEnumerable<AbsolutePath> searchLocations, 
            BuildParameters.IBuildParameters buildParameters)
        {
            AbsolutePath toolDirectory = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, RelativePathToGraphConstructionTool).GetParent(Context.PathTable);
            string pathToTool = Configuration.Layout.BuildEngineDirectory.Combine(Context.PathTable, RelativePathToGraphConstructionTool).ToString(Context.PathTable);
            string outputDirectory = outputFile.GetParent(Context.PathTable).ToString(Context.PathTable);
            string outputFileString = outputFile.ToString(Context.PathTable);
            string enlistmentRoot = m_resolverSettings.Root.ToString(Context.PathTable);
            IReadOnlyCollection<string> entryPointTargets = m_resolverSettings.InitialTargets ?? CollectionUtilities.EmptyArray<string>();

            var requestedQualifiers = m_requestedQualifiers.Select(qualifierId => MsBuildResolverUtils.CreateQualifierAsGlobalProperties(qualifierId, Context)).ToList();

            var arguments = new MSBuildGraphBuilderArguments(
                enlistmentRoot,
                projectEntryPoints.Select(entryPoint => entryPoint.ToString(Context.PathTable)).ToList(),
                outputFileString,
                new GlobalProperties(m_resolverSettings.GlobalProperties ?? CollectionUtilities.EmptyDictionary<string, string>()),
                searchLocations.Select(location => location.ToString(Context.PathTable)).ToList(),
                entryPointTargets,
                requestedQualifiers);

            var responseFilePath = responseFile.ToString(Context.PathTable);
            SerializeResponseFile(responseFilePath, arguments);

            Tracing.Logger.Log.LaunchingGraphConstructionTool(Context.LoggingContext, m_resolverSettings.Location(Context.PathTable), arguments.ToString(), pathToTool);

            // Just being defensive, make sure there is not an old output file lingering around
            File.Delete(outputFileString);

            return FrontEndUtilities.RunSandboxedToolAsync(
                Context,
                pathToTool,
                buildStorageDirectory: outputDirectory,
                fileAccessManifest: GenerateFileAccessManifest(toolDirectory, outputFile),
                arguments: I($"\"{responseFilePath}\""),
                workingDirectory: outputDirectory,
                description: "MsBuild graph builder",
                buildParameters,
                beforeLaunch: () => ConnectToServerPipeAndLogProgress(outputFileString));
        }

        private void SerializeResponseFile(string responseFile, MSBuildGraphBuilderArguments arguments)
        {
            var serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);
            using (var sw = new StreamWriter(responseFile))
            using (var writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(sw, arguments);
            }
        }

        private void ConnectToServerPipeAndLogProgress(string outputFileString)
        {
            // We start a dedicated thread that listens to the graph construction progress process pipe and redirects all messages
            // to BuildXL logging. The thread terminates then the pip is closed or the user requests a cancellation.
            Analysis.IgnoreResult(
                Task.Factory.StartNew(
                        () =>
                        {
                            try
                            {
                                // The name of the pipe is the filename of the output file 
                                using (var pipeClient = new NamedPipeClientStream(
                                    ".",
                                    Path.GetFileName(outputFileString),
                                    PipeDirection.In,
                                    PipeOptions.Asynchronous))
                                using (var reader = new StreamReader(pipeClient, Encoding.UTF8))
                                {
                                    // Let's give the client a 5 second timeout to connect to the graph construction process
                                    pipeClient.Connect(5000);
                                    // We try to read from the pipe while the stream is not flagged to be finished and there is
                                    // no user cancellation
                                    while (!Context.CancellationToken.IsCancellationRequested && !reader.EndOfStream)
                                    {
                                        var line = reader.ReadLine();
                                        if (line != null)
                                        {
                                            Tracing.Logger.Log.ReportGraphConstructionProgress(Context.LoggingContext, line);
                                        }
                                    }
                                }
                            }
                            // In case of a timeout or an unexpected exception, we just log warnings. This only prevents
                            // progress to be reported, but the graph construction process itself may continue to run
                            catch (TimeoutException)
                            {
                                Tracing.Logger.Log.CannotGetProgressFromGraphConstructionDueToTimeout(Context.LoggingContext);
                            }
                            catch (IOException ioException)
                            {
                                Tracing.Logger.Log.CannotGetProgressFromGraphConstructionDueToUnexpectedException(Context.LoggingContext, ioException.Message);
                            }
                        }
                    )
                );
        }

        private FileAccessManifest GenerateFileAccessManifest(AbsolutePath toolDirectory, AbsolutePath outputFile)
        {
            // We make no attempt at understanding what the graph generation process is going to do
            // We just configure the manifest to not fail on unexpected accesses, so they can be collected
            // later if needed
            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorNtCreateFile = true,
                MonitorZwCreateOpenQueryFile = true,
                MonitorChildProcesses = true,
            };

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(Context.PathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAllButSymlinkCreation);

            fileAccessManifest.AddScope(toolDirectory, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowReadAlways);
            fileAccessManifest.AddPath(outputFile, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowWrite);

            return fileAccessManifest;
        }

        /// <nodoc />
        private sealed class MsBuildGraphBuildStorage : ISandboxedProcessFileStorage
        {
            private readonly string m_directory;

            /// <nodoc />
            public MsBuildGraphBuildStorage(string directory)
            {
                m_directory = directory;
            }

            /// <inheritdoc />
            public string GetFileName(SandboxedProcessFile file)
            {
                return Path.Combine(m_directory, file.DefaultFileName());
            }
        }
    }
}

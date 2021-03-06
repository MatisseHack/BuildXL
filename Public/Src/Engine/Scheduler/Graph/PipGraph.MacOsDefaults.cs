﻿using System;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using MacPaths = BuildXL.Interop.MacOS.IO;

namespace BuildXL.Scheduler.Graph
{
    partial class PipGraph
    {
        /// <nodoc />
        public class MacOsDefaults
        {
            private static readonly SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> s_emptySealContents
                = CollectionUtilities.EmptySortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>(OrdinalFileArtifactComparer.Instance);

            private readonly PipProvenance m_provenance;
            private readonly DirectoryArtifact[] m_inputDirectories;
            private readonly FileArtifact[] m_untrackedFiles;
            private readonly DirectoryArtifact[] m_untrackedDirectories;

            /// <nodoc />
            public MacOsDefaults(PathTable pathTable, PipGraph.Builder pipGraph)
            {
                m_provenance = new PipProvenance(
                    0,
                    ModuleId.Invalid,
                    StringId.Invalid,
                    FullSymbol.Invalid,
                    LocationData.Invalid,
                    QualifierId.Unqualified,
                    PipData.Invalid);

                // Sealed Source inputs
                m_inputDirectories =
                    new[]
                    {
                        GetSourceSeal(pathTable, pipGraph, MacPaths.Applications),
                        GetSourceSeal(pathTable, pipGraph, MacPaths.Library),
                        GetSourceSeal(pathTable, pipGraph, MacPaths.UserProvisioning),
                        GetSourceSeal(pathTable, pipGraph, MacPaths.UsrBin),
                        GetSourceSeal(pathTable, pipGraph, MacPaths.UsrInclude),
                        GetSourceSeal(pathTable, pipGraph, MacPaths.UsrLib),
                    };

                m_untrackedFiles =
                    new[]
                    {
                        // login.keychain is created by the OS the first time any process invokes an OS API that references the keychain.
                        // Untracked because build state will not be stored there and code signing will fail if required certs are in the keychain
                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, MacPaths.Etc)),
                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, MacPaths.UserKeyChainsDb)),
                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, MacPaths.UserKeyChains)),
                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, MacPaths.UserCFTextEncoding)),
                        FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, MacPaths.TmpDir)),
                        
                    };

                m_untrackedDirectories =
                    new[]
                    {
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.Bin),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.Dev),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.Private),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.Sbin),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.SystemLibrary),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.UsrLibexec),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.UsrShare),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.UsrStandalone),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.UsrSbin),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.Var),
                        DirectoryArtifact.CreateWithZeroPartialSealId(pathTable, MacPaths.UserPreferences),
                    };
            }

            /// <summary>
            /// Augments the processBuilder with the OS dependencies
            /// </summary>
            public void ProcessDefaults(ProcessBuilder processBuilder)
            {
                if ((processBuilder.Options & Process.Options.DependsOnCurrentOs) != 0)
                {
                    foreach (var inputDirectory in m_inputDirectories)
                    {
                        processBuilder.AddInputDirectory(inputDirectory);
                    }

                    foreach (var untrackedFile in m_untrackedFiles)
                    {
                        processBuilder.AddUntrackedFile(untrackedFile);
                    }

                    foreach (var untrackedDirectory in m_untrackedDirectories)
                    {
                        processBuilder.AddUntrackedDirectoryScope(untrackedDirectory);
                    }
                }
            }

            private DirectoryArtifact GetSourceSeal(PathTable pathTable, PipGraph.Builder pipGraph, string path)
            {
                var sealDirectory = new SealDirectory(
                    AbsolutePath.Create(pathTable, path),
                    contents: s_emptySealContents,
                    kind: SealDirectoryKind.SourceAllDirectories,
                    provenance: m_provenance,
                    tags: ReadOnlyArray<StringId>.Empty,
                    patterns: ReadOnlyArray<StringId>.Empty,
                    scrub: false);

                return pipGraph.AddSealDirectory(sealDirectory);
            }
        }
    }
}

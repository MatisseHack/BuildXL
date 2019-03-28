﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Resolver for static graph Ninja based builds.
    /// </summary>
    public class NinjaResolver : DScriptInterpreterBase, IResolver
    {
        private readonly string m_frontEndName;
        private NinjaWorkspaceResolver m_ninjaWorkspaceResolver;
        private INinjaResolverSettings m_ninjaResolverSettings;
        private ModuleDefinition ModuleDef => m_ninjaWorkspaceResolver.ComputedGraph.Result.ModuleDefinition;

        /// <nodoc/>
        public NinjaResolver(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            Logger logger,
            string frontEndName)
            : base(constants, sharedModuleRegistry, statistics, logger, host, context, configuration)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));
            m_frontEndName = frontEndName;

        }

        /// <inheritdoc/>
        public Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);
            Name = resolverSettings.Name;

            m_ninjaResolverSettings = resolverSettings as INinjaResolverSettings; 
            m_ninjaWorkspaceResolver = workspaceResolver as NinjaWorkspaceResolver;
            
            // TODO: Failure cases, logging
            
            return Task.FromResult<bool>(true);
        }


        /// <inheritdoc/>
        public void LogStatistics()
        {
        }

        /// <inheritdoc/>
        public Task<bool?> TryConvertModuleToEvaluationAsync(ParsedModule module, IWorkspace workspace)
        {
            // No conversion needed.
            return Task.FromResult<bool?>(true);
        }

        /// <inheritdoc/>
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition iModule, QualifierId qualifierId)
        {
            if (!iModule.Equals(ModuleDef))
            {
                return null;
            }

            // Note: we are effectively evaluating iModule
            // Using await to suppress compiler warnings
            // TODO: Async?
            return await Task.FromResult(TryEvaluate(ModuleDef, qualifierId)); 
        }

        private bool? TryEvaluate(ModuleDefinition module, QualifierId qualifierId)    // TODO: Async?
        {
            
            NinjaGraphWithModuleDefinition result = m_ninjaWorkspaceResolver.ComputedGraph.Result;
            
            // TODO: Actual filtering. For now [first development only] we schedule all pips because we are dealing with a single spec 'all'
            IReadOnlyCollection<NinjaNode> filteredNodes = result.Graph.Nodes;

            var graphConstructor = new NinjaPipGraphBuilder(Context, 
                FrontEndHost, 
                ModuleDef, 
                m_ninjaWorkspaceResolver.ProjectRoot, 
                m_ninjaWorkspaceResolver.SpecFile, 
                qualifierId, 
                m_frontEndName, 
                m_ninjaResolverSettings.RemoveAllDebugFlags ?? false,
                m_ninjaResolverSettings.UntrackingSettings);

            return graphConstructor.TrySchedulePips(filteredNodes, qualifierId);
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            // Nothing to do.
        }
       
    }
}

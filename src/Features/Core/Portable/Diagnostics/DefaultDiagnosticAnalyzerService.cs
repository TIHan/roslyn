﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics.EngineV2;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Options;
using Microsoft.CodeAnalysis.SolutionCrawler;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    [Shared]
    [ExportIncrementalAnalyzerProvider(WellKnownSolutionCrawlerAnalyzers.Diagnostic, workspaceKinds: null)]
    internal partial class DefaultDiagnosticAnalyzerService : IIncrementalAnalyzerProvider, IDiagnosticUpdateSource
    {
        private readonly IDiagnosticAnalyzerService _analyzerService;

        [ImportingConstructor]
        public DefaultDiagnosticAnalyzerService(
            IDiagnosticAnalyzerService analyzerService,
            IDiagnosticUpdateSourceRegistrationService registrationService)
        {
            _analyzerService = analyzerService;
            registrationService.Register(this);
        }

        public IIncrementalAnalyzer CreateIncrementalAnalyzer(Workspace workspace)
        {
            if (!workspace.Options.GetOption(ServiceComponentOnOffOptions.DiagnosticProvider))
            {
                return null;
            }

            return new DefaultDiagnosticAnalyzer(this, workspace);
        }

        public event EventHandler<DiagnosticsUpdatedArgs> DiagnosticsUpdated;

        // this only support push model, pull model will be provided by DiagnosticService by caching everything this one pushed
        public bool SupportGetDiagnostics => false;

        public ImmutableArray<DiagnosticData> GetDiagnostics(Workspace workspace, ProjectId projectId, DocumentId documentId, object id, bool includeSuppressedDiagnostics = false, CancellationToken cancellationToken = default)
        {
            // pull model not supported
            return ImmutableArray<DiagnosticData>.Empty;
        }

        internal void RaiseDiagnosticsUpdated(DiagnosticsUpdatedArgs state)
        {
            this.DiagnosticsUpdated?.Invoke(this, state);
        }

        private class DefaultDiagnosticAnalyzer : IIncrementalAnalyzer
        {
            private readonly DefaultDiagnosticAnalyzerService _service;
            private readonly Workspace _workspace;

            public DefaultDiagnosticAnalyzer(DefaultDiagnosticAnalyzerService service, Workspace workspace)
            {
                _service = service;
                _workspace = workspace;
            }

            public bool NeedsReanalysisOnOptionChanged(object sender, OptionChangedEventArgs e)
            {
                if (e.Option == InternalRuntimeDiagnosticOptions.Syntax ||
                    e.Option == InternalRuntimeDiagnosticOptions.Semantic ||
                    e.Option == InternalRuntimeDiagnosticOptions.ScriptSemantic)
                {
                    return true;
                }

                return false;
            }

            public async Task AnalyzeSyntaxAsync(Document document, InvocationReasons reasons, CancellationToken cancellationToken)
            {
                Debug.Assert(document.Project.Solution.Workspace == _workspace);

                // right now, there is no way to observe diagnostics for closed file.
                if (!_workspace.IsDocumentOpen(document.Id) ||
                    !_workspace.Options.GetOption(InternalRuntimeDiagnosticOptions.Syntax))
                {
                    return;
                }

                await AnalyzeForKind(document, AnalysisKind.Syntax, cancellationToken).ConfigureAwait(false);
            }

            public async Task AnalyzeDocumentAsync(Document document, SyntaxNode bodyOpt, InvocationReasons reasons, CancellationToken cancellationToken)
            {
                Debug.Assert(document.Project.Solution.Workspace == _workspace);

                if (!IsSemanticAnalysisOn(document))
                {
                    return;
                }

                await AnalyzeForKind(document, AnalysisKind.Syntax, cancellationToken).ConfigureAwait(false);
            }

            private async Task AnalyzeForKind(Document document, AnalysisKind kind, CancellationToken cancellationToken)
            {
                var diagnosticData = await GetDiagnsoticsForKindAsync(document, kind, cancellationToken).ConfigureAwait(false);

                _service.RaiseDiagnosticsUpdated(
                    DiagnosticsUpdatedArgs.DiagnosticsCreated(new DefaultUpdateArgsId(_workspace.Kind, kind, document.Id),
                    _workspace, document.Project.Solution, document.Project.Id, document.Id, diagnosticData.ToImmutableArrayOrEmpty()));
            }

            private Task<IEnumerable<DiagnosticData>> GetDiagnsoticsForKindAsync(Document document, AnalysisKind kind, CancellationToken cancellationToken)
            {
                // C# or VB document that supports compiler
                var compilerAnalyzer = _service._analyzerService.GetCompilerDiagnosticAnalyzer(document.Project.Language);
                if (compilerAnalyzer != null)
                {
                    return DiagnosticIncrementalAnalyzer.GetDiagnosticsAsync(_service._analyzerService, document, SpecializedCollections.SingletonEnumerable(compilerAnalyzer), kind, cancellationToken);
                }

                // document that doesn't support compiler diagnostics such as fsharp or typescript
                var analyzers = _service._analyzerService.GetDiagnosticAnalyzers(document.Project);
                return DiagnosticIncrementalAnalyzer.GetDiagnosticsAsync(_service._analyzerService, document, analyzers, kind, cancellationToken);
            }

            public void RemoveDocument(DocumentId documentId)
            {
                // a file is removed from misc project
                RaiseEmptyDiagnosticUpdated(AnalysisKind.Syntax, documentId);
                RaiseEmptyDiagnosticUpdated(AnalysisKind.Semantic, documentId);
            }

            public Task DocumentResetAsync(Document document, CancellationToken cancellationToken)
            {
                // no closed file diagnostic and file is not opened, remove any existing diagnostics
                RemoveDocument(document.Id);
                return Task.CompletedTask;
            }

            public Task DocumentCloseAsync(Document document, CancellationToken cancellationToken)
            {
                return DocumentResetAsync(document, cancellationToken);
            }

            private void RaiseEmptyDiagnosticUpdated(AnalysisKind kind, DocumentId documentId)
            {
                _service.RaiseDiagnosticsUpdated(DiagnosticsUpdatedArgs.DiagnosticsRemoved(
                    new DefaultUpdateArgsId(_workspace.Kind, kind, documentId), _workspace, null, documentId.ProjectId, documentId));
            }

            private bool IsSemanticAnalysisOn(Document document)
            {
                // right now, there is no way to observe diagnostics for closed file.
                if (!_workspace.IsDocumentOpen(document.Id))
                {
                    return false;
                }

                if (_workspace.Options.GetOption(InternalRuntimeDiagnosticOptions.Semantic))
                {
                    return true;
                }

                return _workspace.Options.GetOption(InternalRuntimeDiagnosticOptions.ScriptSemantic) && document.SourceCodeKind == SourceCodeKind.Script;
            }

            public Task AnalyzeProjectAsync(Project project, bool semanticsChanged, InvocationReasons reasons, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task DocumentOpenAsync(Document document, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task NewSolutionSnapshotAsync(Solution solution, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void RemoveProject(ProjectId projectId)
            {
            }

            private class DefaultUpdateArgsId : BuildToolId.Base<int, DocumentId>, ISupportLiveUpdate
            {
                private readonly string _workspaceKind;

                public DefaultUpdateArgsId(string workspaceKind, AnalysisKind kind, DocumentId documentId) : base((int)kind, documentId)
                {
                    _workspaceKind = workspaceKind;
                }

                public override string BuildTool => PredefinedBuildTools.Live;

                public override bool Equals(object obj)
                {
                    var other = obj as DefaultUpdateArgsId;
                    if (other == null)
                    {
                        return false;
                    }

                    return _workspaceKind == other._workspaceKind && base.Equals(obj);
                }

                public override int GetHashCode()
                {
                    return Hash.Combine(_workspaceKind.GetHashCode(), base.GetHashCode());
                }
            }
        }
    }
}

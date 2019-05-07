﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.ExternalAccess.FSharp.Diagnostics;

namespace Microsoft.CodeAnalysis.ExternalAccess.FSharp.Internal.Diagnostics
{
    [DiagnosticAnalyzer(LanguageNames.FSharp)]
    internal class FSharpDocumentDiagnosticAnalyzer : DocumentDiagnosticAnalyzer, IBuiltInAnalyzer
    {
        private readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics;

        public FSharpDocumentDiagnosticAnalyzer()
        {
            _supportedDiagnostics = CreateSupportedDiagnostics();
        }

        static public ImmutableArray<DiagnosticDescriptor> CreateSupportedDiagnostics()
        {
            // We are constructing our own descriptors at run-time. Compiler service is already doing error formatting and localization.
            var dummyDescriptors = ImmutableArray.CreateBuilder<DiagnosticDescriptor>();
            for (var i = 0; i <= 10000; i++)
            {
                dummyDescriptors.Add(new DiagnosticDescriptor(String.Format("FS{0:D4}", i), String.Empty, String.Empty, String.Empty, DiagnosticSeverity.Error, true, null, null));
            }
            return dummyDescriptors.ToImmutable();
        }

        public override int Priority => 10; // Default = 50

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => _supportedDiagnostics;

        public override Task<ImmutableArray<Diagnostic>> AnalyzeSemanticsAsync(Document document, CancellationToken cancellationToken)
        {
            var analyzer = document.Project.LanguageServices.GetService<IFSharpDocumentDiagnosticAnalyzer>();
            if (analyzer == null)
            {
                return Task.FromResult(ImmutableArray<Diagnostic>.Empty);
            }

            return analyzer.AnalyzeSemanticsAsync(document, cancellationToken);
        }

        public override Task<ImmutableArray<Diagnostic>> AnalyzeSyntaxAsync(Document document, CancellationToken cancellationToken)
        {
            var analyzer = document.Project.LanguageServices.GetService<IFSharpDocumentDiagnosticAnalyzer>();
            if (analyzer == null)
            {
                return Task.FromResult(ImmutableArray<Diagnostic>.Empty);
            }

            return analyzer.AnalyzeSyntaxAsync(document, cancellationToken);
        }

        public DiagnosticAnalyzerCategory GetAnalyzerCategory()
        {
            return DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;
        }

        public bool OpenFileOnly(Workspace workspace)
        {
            return true;
        }
    }
}

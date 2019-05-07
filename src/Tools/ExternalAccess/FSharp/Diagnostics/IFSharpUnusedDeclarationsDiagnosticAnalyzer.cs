﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.CodeAnalysis.ExternalAccess.FSharp.Diagnostics
{
    internal interface IFSharpUnusedDeclarationsDiagnosticAnalyzer : ILanguageService
    {
        Task<ImmutableArray<Diagnostic>> AnalyzeSemanticsAsync(DiagnosticDescriptor descriptor, Document document, CancellationToken cancellationToken);
    }
}

﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if RAZOR_EXTENSION_DEVELOPER_MODE
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;

namespace Microsoft.VisualStudio.LanguageServices.Razor
{
    internal interface IRazorEngineDirectiveResolver
    {
        Task<IEnumerable<DirectiveDescriptor>> GetRazorEngineDirectivesAsync(Workspace workspace, Project project, CancellationToken cancellationToken = default(CancellationToken));
    }
}
#endif
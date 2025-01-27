﻿//-----------------------------------------------------------------------------
// FILE:	    GitHubPackageVisibility.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Runtime.Serialization;

using Neon.Common;

namespace Neon.Deployment
{
    /// <summary>
    /// Enumerates the supported GitHub package visibility types.
    /// </summary>
    public enum GitHubPackageVisibility
    {
        /// <summary>
        /// All packages.
        /// </summary>
        [EnumMember(Value = "all")]
        All,

        /// <summary>
        /// Public packages.
        /// </summary>
        [EnumMember(Value = "public")]
        Public,

        /// <summary>
        /// Private packages.
        /// </summary>
        [EnumMember(Value = "private")]
        Private,

        /// <summary>
        /// Internal packages.
        /// </summary>
        [EnumMember(Value = "internal")]
        Internal
    }
}

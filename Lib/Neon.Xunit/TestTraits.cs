﻿//-----------------------------------------------------------------------------
// FILE:	    TestTraits.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Neon.Xunit
{
    /// <summary>
    /// Identifies the common neonFORGE related test traits.
    /// </summary>
    public static class TestTraits
    {
        /// <summary>
        /// Identifies the test project.  See <see cref="TestProject"/> for the standard
        /// project names.
        /// </summary>
        public const string Project = "project";

        /// <summary>
        /// Identifies slow tests by setting this trait's value to <b>"true"</b>.
        /// </summary>
        public const string Slow = "slow";

        /// <summary>
        /// Identifies unreliable tests that are buggy or experience transient problems
        /// by setting this trait's value to <b>"true"</b>.
        /// </summary>
        public const string Unreliable = "unreliable";
    }
}

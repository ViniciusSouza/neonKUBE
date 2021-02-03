﻿//-----------------------------------------------------------------------------
// FILE:        CadenceTestHelper.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.
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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Xunit;
using Neon.Xunit.Cadence;

using Newtonsoft.Json;
using Xunit;

using Test.Neon.Models;
using Newtonsoft.Json.Linq;

namespace TestCadence
{
    /// <summary>
    /// Internal unit test helpers.
    /// </summary>
    internal static class CadenceTestHelper
    {
        /// <summary>
        /// <para>
        /// Controls whether <see cref="CadenceFixture"/> should be configured to leave
        /// the Cadence test container running after the unit tests complete.  This can
        /// be handy during debugging by keeping the Cadence UX around for post test
        /// investigations.
        /// </para>
        /// <note>
        /// This should be disabled for normal CI/CD environments.
        /// </note>
        /// </summary>
        public const bool KeepCadenceServerOpen = true;

        /// <summary>
        /// Specifies the log level to use for Cadence related unit tests.
        /// </summary>
        public static readonly LogLevel LogLevel = LogLevel.Info;

        /// <summary>
        /// Specifies whether Cadence unit tests will connect to Cadence in DEBUG mode.
        /// </summary>
        public const bool Debug = false;

        /// <summary>
        /// <para>
        /// Optionally runs Cadence in prelaunched mode for unit testing.
        /// </para>
        /// <note>
        /// This must always be reset to <c>false</c> after any manual debugging so
        /// that unit tests will work properly.
        /// </note>
        /// </summary>
        public const bool DebugPrelaunched = false;

        /// <summary>
        /// <para>
        /// Optionally runs Cadence in without heartbeats for unit testing.
        /// </para>
        /// <note>
        /// This must always be reset to <c>false</c> after any manual debugging so
        /// that unit tests will work properly.
        /// </note>
        /// </summary>
        public const bool DebugDisableHeartbeats = false;

        /// <summary>
        /// The Cadence task list to be used for test workers.
        /// </summary>
        public const string TaskList = "tests";
    }
}

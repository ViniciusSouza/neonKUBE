﻿//-----------------------------------------------------------------------------
// FILE:	    ClusterCommand.cs
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;

using Neon.Common;
using Neon.Kube;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>cluster</b> command.
    /// </summary>
    [Command]
    public class ClusterCommand : CommandBase
    {
        private const string usage = @"
Performs basic cluster provisioning and management.

USAGE:

    neon cluster prepare    - Prepares cluster infrastructure
    neon cluster setup      - Creates a cluster
    neon cluster verify     - Verifies a cluster definition
";

        /// <inheritdoc/>
        public override string[] Words => new string[] { "cluster" };

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override async Task RunAsync(CommandLine commandLine)
        {
            Help();
            await Task.CompletedTask;
        }
    }
}

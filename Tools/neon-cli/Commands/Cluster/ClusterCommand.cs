﻿//-----------------------------------------------------------------------------
// FILE:	    ClusterCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

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
using Neon.Hive;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>cluster</b> command.
    /// </summary>
    public class ClusterCommand : CommandBase
    {
        private const string usage = @"
Performs basic cluster provisioning and management.

USAGE:

    neon cluster example    - Outputs a sample cluster definition
    neon cluster get        - Gets cluster variables and settings
    neon cluster info       - Outputs cluster information
    neon cluster node       - Manages cluster nodes
    neon cluster prepare    - Prepares environment for cluster setup
    neon cluster registry   - Manages a local Docker registry
    neon cluster set        - Sets a cluster variable or setting
    neon cluster setup      - Deploys a cluster
    neon cluster verify     - Verifies a cluster definition
";

        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "cluster" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            Help();
        }

        /// <inheritdoc/>
        public override DockerShimInfo Shim(DockerShim shim)
        {
            return new DockerShimInfo(isShimmed: true);
        }
    }
}

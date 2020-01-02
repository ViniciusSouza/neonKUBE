﻿//-----------------------------------------------------------------------------
// FILE:	    LoginImportCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2020 by neonFORGE, LLC.  All rights reserved.
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
    /// Implements the <b>login import</b> command.
    /// </summary>
    public class LoginImportCommand : CommandBase
    {
        private const string usage = @"
Imports an extended Kubernetes context from a file generated by
a previous [neon login export] command.

USAGE:

    neon login import [--nologin] [--force] PATH

ARGUMENTS:

    PATH        - Path to the context file.

OPTIONS:

    --force     - Don't prompt to replace an existing context.
    --nologin   - Don't log into the imported cluster.
";

        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "login", "import" }; }
        }

        /// <inheritdoc/>
        public override string[] ExtendedOptions
        {
            get { return new string[] { "--nologin", "--force" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            if (commandLine.Arguments.Length < 1)
            {
                Console.Error.WriteLine("*** ERROR: PATH is required.");
                Program.Exit(1);
            }

            var newLogin        = NeonHelper.YamlDeserialize<KubeLogin>(File.ReadAllText(commandLine.Arguments.First()));
            var existingContext = KubeHelper.Config.GetContext(newLogin.Context.Name);

            // $todo(jefflill():
            //
            // This is a bit odd.  Why didn't we serialize this here originally?

            newLogin.Context.Extension = newLogin.Extensions;

            // Add/replace the context.

            if (existingContext != null)
            {
                if (!commandLine.HasOption("--force") && !Program.PromptYesNo($"*** Are you sure you want to replace [{existingContext.Name}]?"))
                {
                    return;
                }

                KubeHelper.Config.RemoveContext(existingContext);
            }

            KubeHelper.Config.Contexts.Add(newLogin.Context);

            // Add/replace the cluster.

            var existingCluster = KubeHelper.Config.GetCluster(newLogin.Context.Properties.Cluster);

            if (existingCluster != null)
            {
                KubeHelper.Config.Clusters.Remove(existingCluster);
            }

            KubeHelper.Config.Clusters.Add(newLogin.Cluster);

            // Add/replace the user.

            var existingUser = KubeHelper.Config.GetUser(newLogin.Context.Properties.User);

            if (existingUser != null)
            {
                KubeHelper.Config.Users.Remove(existingUser);
            }

            KubeHelper.Config.Users.Add(newLogin.User);
            KubeHelper.Config.Save();

            Console.Error.WriteLine($"Imported: {newLogin.Context.Name}");

            if (commandLine.GetOption("--nologin") == null)
            {
                Console.Error.WriteLine($"Logging into: {newLogin.Context.Name}");
                KubeHelper.Config.CurrentContext = newLogin.Context.Name;
                KubeHelper.Config.Save();
            }
        }
    }
}

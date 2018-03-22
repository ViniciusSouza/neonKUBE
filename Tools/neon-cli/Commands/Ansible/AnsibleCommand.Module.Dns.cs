﻿//-----------------------------------------------------------------------------
// FILE:	    AnsibleCommand.Module.Dns.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using Neon.Cluster;
using Neon.Cryptography;
using Neon.Common;
using Neon.IO;
using Neon.Net;

namespace NeonCli
{
    public partial class AnsibleCommand : CommandBase
    {
        //---------------------------------------------------------------------
        // neon_dns:
        //
        // Synopsis:
        // ---------
        //
        // Manages neonCLUSTER DNS host entries.
        //
        // Requirements:
        // -------------
        //
        // This module runs only within the [neon-cli] container when invoked
        // by [neon ansible exec ...] or [neon ansible play ...].
        //
        // Options:
        // --------
        //
        // parameter    required    default     choices     comments
        // --------------------------------------------------------------------
        //
        // hostname     yes                                 DNS hostname
        //
        // target       see comment                         DNS target structured as YAML.  
        //                                                  Required when [state=present]
        //
        // state        no          present     absent      indicates whether the DNS target
        //                                      present     should be created or removed

        /// <summary>
        /// Implements the built-in <b>neon_dns</b> module.
        /// </summary>
        /// <param name="context">The module execution context.</param>
        private void RunDnsModule(ModuleContext context)
        {
            var cluster = NeonClusterHelper.Cluster;
            var consul  = NeonClusterHelper.Consul;

            // Obtain common arguments.

            context.WriteLine(Verbosity.Trace, $"Parsing [hostname]");

            if (!context.Arguments.TryGetValue<string>("hostname", out var hostname))
            {
                throw new ArgumentException($"[hostname] module argument is required.");
            }

            if (!ClusterDefinition.DnsHostRegex.IsMatch(hostname))
            {
                throw new ArgumentException($"[hostname={hostname}] is not a valid DNS hostname.");
            }

            context.WriteLine(Verbosity.Trace, $"Parsing [state]");

            if (!context.Arguments.TryGetValue<string>("state", out var state))
            {
                state = "present";
            }

            state = state.ToLowerInvariant();

            context.WriteLine(Verbosity.Trace, $"Parsing [target]");

            if (!context.Arguments.TryGetValue<string>("target", out var proxy) && state == "present")
            {
                throw new ArgumentException($"[target] module argument is required when [state={state}].");
            }

            // We have the required arguments, so perform the operation.

            var targetKey = $"neon/dns/targets/{hostname}";

            switch (state)
            {
                case "absent":

                    context.WriteLine(Verbosity.Trace, $"Check if DNS target [{hostname}] exists.");

                    if (consul.KV.Exists(targetKey).Result)
                    {
                        context.WriteLine(Verbosity.Trace, $"DNS target [{hostname}] does exist.");
                        context.WriteLine(Verbosity.Info, $"Deleting DNS target [{hostname}].");

                        if (!context.CheckMode)
                        {
                            consul.KV.Delete(targetKey);
                            context.WriteLine(Verbosity.Trace, $"DNS target [{hostname}] deleted.");
                        }

                        context.Changed = true;
                    }
                    else
                    {
                        context.WriteLine(Verbosity.Trace, $"DNS target [{hostname}] does not exist.");
                    }
                    break;

                case "present":

                    context.WriteLine(Verbosity.Trace, $"Parsing [target]");

                    if (!context.Arguments.TryGetValue<JObject>("target", out var targetObject))
                    {
                        throw new ArgumentException($"[target] module argument is required.");
                    }

                    var targetText = targetObject.ToString();

                    context.WriteLine(Verbosity.Trace, "Parsing target");

                    var newTarget = NeonHelper.JsonOrYamlDeserialize<DnsTarget>(targetText, strict: true);

                    context.WriteLine(Verbosity.Trace, "Target parsed successfully");

                    // Use the target hostname argument if the deserialized target doesn't
                    // have a name.  This will make it easier on operators because 
                    // they won't need to specify the name twice.

                    if (string.IsNullOrWhiteSpace(newTarget.Hostname))
                    {
                        newTarget.Hostname = hostname;
                    }

                    // Ensure that the target name passed as an argument and the
                    // name within the target definition match.

                    if (!string.Equals(hostname, newTarget.Hostname, StringComparison.InvariantCultureIgnoreCase))
                    {
                        throw new ArgumentException($"The [hostname={hostname}] argument and the target's [{nameof(DnsTarget.Hostname)}={newTarget.Hostname}] property are not the same.");
                    }

                    context.WriteLine(Verbosity.Trace, "Target hostname matched.");

                    // Validate the DNS target.

                    context.WriteLine(Verbosity.Trace, "Validating DNS target.");

                    var errors = newTarget.Validate(cluster.Definition, cluster.Definition.GetNodeGroups(excludeAllGroup: true));

                    if (errors.Count > 0)
                    {
                        context.WriteLine(Verbosity.Trace, $"[{errors.Count}] DNS target validation errors.");

                        foreach (var error in errors)
                        {
                            context.WriteLine(Verbosity.Important, error);
                            context.WriteErrorLine(error);
                        }

                        context.Failed = true;
                        return;
                    }

                    context.WriteLine(Verbosity.Trace, "DNS target is valid.");

                    // Try reading any existing target with this name and then determine
                    // whether the two versions of the target are actually different. 

                    context.WriteLine(Verbosity.Trace, $"Looking for existing target for [{hostname}]");

                    var existingTarget = consul.KV.GetObjectOrDefault<DnsTarget>(targetKey).Result;

                    if (existingTarget != null)
                    {
                        context.WriteLine(Verbosity.Trace, $"Target exists: checking for differences.");

                        context.Changed = !NeonHelper.JsonEquals(newTarget, existingTarget);

                        if (context.Changed)
                        {
                            context.WriteLine(Verbosity.Trace, $"Targets are different.");
                        }
                        else
                        {
                            context.WriteLine(Verbosity.Info, $"Targets are the same.  No need to update.");
                        }
                    }
                    else
                    {
                        context.Changed = true;
                        context.WriteLine(Verbosity.Trace, $"Target for [hostname={hostname}] does not exist.");
                    }

                    if (context.Changed)
                    {
                        context.WriteLine(Verbosity.Trace, $"Updating target.");
                        consul.KV.PutObject(targetKey, newTarget).Wait();
                        context.WriteLine(Verbosity.Info, $"DNS target updated.");
                    }

                    break;

                default:

                    throw new ArgumentException($"[state={state}] is not one of the valid choices: [absent] or [present].");
            }
        }
    }
}

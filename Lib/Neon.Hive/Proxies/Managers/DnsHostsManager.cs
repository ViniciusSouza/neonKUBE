﻿//-----------------------------------------------------------------------------
// FILE:	    DnsHostsManager.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;

using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Time;

namespace Neon.Hive
{
    /// <summary>
    /// Handles local hive DNS hosts related operations for a <see cref="HiveProxy"/>.
    /// </summary>
    public sealed class DnsHostsManager
    {
        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Returns the appropriate timeout to use when waiting for DNS host entry
        /// changes to propagate through the hive.  This is currently set to
        /// 60 seconds but may change in the future.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It may take some time for changes made to the hive DNS to be
        /// reflected in the DNS names actually resolved on the hive nodes.
        /// Here's an outline of the process:
        /// </para>
        /// <list type="number">
        /// <item>
        /// The [neon-dns-mon] service checks the hive DNS entries for 
        /// changes every 5 seconds, performs any required endpoint health 
        /// checks and then writes the actual DNS host to address mappings
        /// to Consul.
        /// </item>
        /// <item>
        /// The [neon-dns] service running on each hive manager, checks
        /// the Consul host/address mappings generated by [neon-dns-mon]
        /// for changes every 5 seconds.  When changes is detected, 
        /// [neon-dns] creates a local file on the managers signalling
        /// the change.
        /// </item>
        /// <item>
        /// The [neon-dns-loader] systemd service running locally on each manager
        /// monitors for the signal file created by [neon-dns] when the 
        /// DNS host/address mappings have changed once a second, and signals
        /// the PowerDNS instance on the manager to reload the entries.
        /// </item>
        /// <item>
        /// All hive nodes are configured to use the managers as their
        /// upstream name server so any DNS name resolutions will ultimately
        /// be forwarded to a manager, once and locally cached resolutions
        /// will have expired.  Hive DNS entries are cached for 30 seconds,
        /// so it may take up to 30 seconds for a PowerDNS update to be
        /// consistent on all hive nodes.
        /// </item>
        /// </list>
        /// <para>
        /// As you can see, it can take something like:
        /// </para>
        /// <code>
        /// 5 + 5 + 1 + 30 = 46 seconds
        /// </code>
        /// <para>
        /// For a change to the hive DNS to ultimately be consistent on all
        /// hive nodes.  This method waits 60 seconds to add about 15seconds
        /// for health checks and other overhead.
        /// </para>
        /// </remarks>
        public static TimeSpan PropagationTimeout => TimeSpan.FromSeconds(60);

        /// <summary>
        /// Determines whether a DNS entry name is valid.
        /// </summary>
        /// <param name="name">The name being validated.</param>
        /// <returns><c>true</c> if the name is valid.</returns>
        [Pure]
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return HiveDefinition.DnsHostRegex.IsMatch(name);
        }

        //---------------------------------------------------------------------
        // Instance members

        private HiveProxy hive;

        /// <summary>
        /// Internal constructor.
        /// </summary>
        /// <param name="hive">The parent <see cref="HiveProxy"/>.</param>
        internal DnsHostsManager(HiveProxy hive)
        {
            Covenant.Requires<ArgumentNullException>(hive != null);

            this.hive = hive;
        }

        /// <summary>
        /// Returns a named hive DNS host entry.
        /// </summary>
        /// <param name="hostname">The DNS hostname (case insenstive).</param>
        /// <returns>The <see cref="DnsEntry"/> or <c>null</c> if it doesn't exist.</returns>
        public DnsEntry Get(string hostname)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(hostname));
            Covenant.Requires<ArgumentException>(IsValidName(hostname));

            hostname = hostname.ToLowerInvariant();

            return hive.Consul.Client.KV.GetObjectOrDefault<DnsEntry>($"{HiveConst.ConsulDnsEntriesKey}/{hostname}").Result;
        }

        /// <summary>
        /// Lists the hive DNS host entries.
        /// </summary>
        /// <param name="includeSystem">Optionally include built-in system entries.</param>
        /// <returns>The list of name/entry values.</returns>
        public List<DnsEntry> List(bool includeSystem = false)
        {
            var list = new List<DnsEntry>();

            foreach (var key in hive.Consul.Client.KV.ListKeys(HiveConst.ConsulDnsEntriesKey, ConsulListMode.PartialKey).Result)
            {
                var entry = hive.Consul.Client.KV.GetObjectOrDefault<DnsEntry>($"{HiveConst.ConsulDnsEntriesKey}/{key}").Result;

                if (entry == null)
                {
                    return null;    // It's possible for the key to have been removed since it was listed.
                }

                if (!entry.IsSystem || includeSystem)
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        /// <summary>
        /// Removes a hive DNS host entry.
        /// </summary>
        /// <param name="hostname">The DNS hostname (case insenstive).</param>
        /// <param name="waitUntilPropagated">
        /// Optionally waits for <see cref="PropagationTimeout"/> for the change to be
        /// propagated across the hive.  This defaults to <c>false</c>.
        /// </param>
        public void Remove(string hostname, bool waitUntilPropagated = false)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(hostname));
            Covenant.Requires<ArgumentException>(IsValidName(hostname));

            hostname = hostname.ToLowerInvariant();

            hive.Consul.Client.KV.Delete($"{HiveConst.ConsulDnsEntriesKey}/{hostname}").Wait();

            if (waitUntilPropagated)
            {
                Thread.Sleep(PropagationTimeout);
            }
        }

        /// <summary>
        /// Sets a hive DNS host entry.
        /// </summary>
        /// <param name="entry">The entry definition.</param>
        /// <param name="waitUntilPropagated">
        /// Optionally waits for <see cref="PropagationTimeout"/> for the change to be
        /// propagated across the hive.  This defaults to <c>false</c>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if an existing entry already exists and its <see cref="DnsEntry.IsSystem"/>
        /// value doesn't match that for the new entry.
        /// </exception>
        /// <remarks>
        /// <note>
        /// <para>
        /// This method will not allow a SYSTEM entry to overwrite a non-SYSTEM entry
        /// or vice versa.  This helps prevent accidentially impacting important hive 
        /// services (like the local registry).
        /// </para>
        /// <para>
        /// If you really need to do this, you can remove the existing entry first.
        /// </para>
        /// </note>
        /// </remarks>
        public void Set(DnsEntry entry, bool waitUntilPropagated = false)
        {
            Covenant.Requires<ArgumentNullException>(entry != null);
            Covenant.Requires<ArgumentException>(IsValidName(entry.Hostname));

            var existing = Get(entry.Hostname);

            if (existing != null && existing.IsSystem != entry.IsSystem)
            {
                if (existing.IsSystem)
                {
                    throw new InvalidOperationException($"Cannot overwrite existing SYSTEM DNS entry [{entry.Hostname}] with a non-SYSTEM entry.");
                }
                else
                {
                    throw new InvalidOperationException($"Cannot overwrite existing non-SYSTEM DNS entry [{entry.Hostname}] with a SYSTEM entry.");
                }
            }

            var hostname = entry.Hostname.ToLowerInvariant();

            hive.Consul.Client.KV.PutObject($"{HiveConst.ConsulDnsEntriesKey}/{hostname}", entry).Wait();

            if (waitUntilPropagated)
            {
                Thread.Sleep(PropagationTimeout);
            }
        }

        /// <summary>
        /// Returns the current DNS host/answers as a dictionary.
        /// </summary>
        /// <returns>The answers dictionary.</returns>
        public Dictionary<string, List<string>> GetAnswers()
        {
            var hosts  = hive.Consul.Client.KV.GetStringOrDefault(HiveConst.ConsulDnsHostsKey).Result;
            var answers = new Dictionary<string, List<string>>(StringComparer.InvariantCultureIgnoreCase);

            if (hosts == null)
            {
                return answers;
            }

            var unhealthyPrefix = "# unhealthy:";

            using (var reader = new StringReader(hosts))
            {
                foreach (var line in reader.Lines())
                {
                    if (line.StartsWith(unhealthyPrefix))
                    {
                        // Comment lines formatted like:
                        //
                        //      # unhealthy: HOSTNAME
                        //
                        // Have no health endpoints.  We're going to add an empty
                        // list for these and highlight these below.

                        var host = line.Substring(unhealthyPrefix.Length).Trim();

                        if (!answers.TryGetValue(host, out var addresses))
                        {
                            addresses = new List<string>();
                            answers.Add(host, addresses);
                        }
                    }
                    else if (line.StartsWith("#"))
                    {
                        // Ignore other comment lines.
                    }
                    else
                    {
                        var fields = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                        if (fields.Length != 2)
                        {
                            continue;
                        }

                        var address = fields[0];
                        var host    = fields[1];

                        if (!answers.TryGetValue(host, out var addresses))
                        {
                            addresses = new List<string>();
                            answers.Add(host, addresses);
                        }

                        addresses.Add(address);
                    }
                }
            }

            return answers;
        }
    }
}

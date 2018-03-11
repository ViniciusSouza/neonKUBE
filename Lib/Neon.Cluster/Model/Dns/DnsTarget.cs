﻿//-----------------------------------------------------------------------------
// FILE:	    DnsTarget.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Net;

namespace Neon.Cluster
{
    /// <summary>
    /// Describes a DNS target domain to be served dynamically by the the neonCLUSTER 
    /// Dynamic DNS implementation.  These records are used by the <b>neon-dns-health</b> 
    /// service to persist the <see cref="DnsAnswer"/> records to Consul for the
    /// healthy endpoints.
    /// </summary>
    public class DnsTarget
    {
        private string      domain;
        private string      type;
        private string      contents;
        private int         ttl;

        /// <summary>
        /// The domain name without the terminating period in lowercase.
        /// </summary>
        [JsonProperty(PropertyName = "Domain", Required = Required.Always)]
        public string Domain
        {
            get { return domain; }
            
            set
            {
                Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(value));

                domain = value.ToLowerInvariant();
            }
        }

        /// <summary>
        /// The DNS record type in uppercase (e.g. "A", "CNAME", "MX",...).
        /// </summary>
        [JsonProperty(PropertyName = "Type", Required = Required.Always)]
        public string Type
        {
            get { return type; }

            set
            {
                Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(value));

                type = value.ToUpperInvariant();
            }
        }

        /// <summary>
        /// The record contents.  For A records, this will simply be an IP address.
        /// For CNAME, this will be the referenced domain and for MX records, this
        /// will be the referenced domain followed by the priority.
        /// </summary>
        [JsonProperty(PropertyName = "Contents", Required = Required.Always)]
        public string Contents
        {
            get { return contents; }

            set
            {
                Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(value));

                contents = value;
            }
        }

        /// <summary>
        /// The DNS TTL in seconds.
        /// </summary>
        [JsonProperty(PropertyName = "Ttl", Required = Required.Always)]
        public int Ttl
        {
            get { return ttl; }

            set
            {
                Covenant.Requires<ArgumentException>(value >= 0, $"DNS [TTL={value}] is not valid.");

                ttl = value;
            }
        }

        /// <summary>
        /// Lists the domain endpoints.
        /// </summary>
        [JsonProperty(PropertyName = "Endpoints", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public List<DnsEndpoint> Endpoints { get; set; } = new List<DnsEndpoint>();

        /// <summary>
        /// Returns the Consul key to be used to persist this target.  This
        /// will be formatted as <b>DOMAIN-TYPE</b>.
        /// </summary>
        [JsonIgnore]
        public string Key
        {
            get { return $"{Domain}-{Type}"; }
        }
    }
}

﻿//-----------------------------------------------------------------------------
// FILE:	    AzureHostingManager.cs
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
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.LoadBalancer.Definition;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Net;
using Neon.Time;
using Microsoft.Azure.Management.Monitor.Fluent.Models;
using Microsoft.Azure.Management.ContainerService.Fluent.Models;
using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.CosmosDB.Fluent.Models;
using Couchbase.Annotations;

namespace Neon.Kube
{
    /// <summary>
    /// Manages cluster provisioning on the Google Cloud Platform.
    /// </summary>
    /// <remarks>
    /// </remarks>
    [HostingProvider(HostingEnvironments.Azure)]
    public class AzureHostingManager : HostingManager
    {
        // IMPLEMENTATION NOTE:
        // --------------------
        // Here's the original issue covering Azure provisioning and along with 
        // some discussion about how neonKUBE thinks about cloud deployments:
        // 
        //      https://github.com/nforgeio/neonKUBE/issues/908
        //
        // The remainder of this note will outline how Azure provisioning works.
        //
        // A neonKUBE Azure cluster will require provisioning these things:
        //
        //      * VNET
        //      * VMs & Drives
        //      * Load balancer with public IP
        //
        // In the future, we may relax the public load balancer requirement so
        // that virtual air-gapped clusters can be supported (more on that below).
        //
        // Nodes will be deployed in two Azure availability sets, one set for the
        // masters and the other one for the workers.  We're doing this to ensure
        // that there will always be a quorum of masters available during planned
        // Azure maintenance.
        //
        // By default, we're also going to create an Azure proximity placement group
        // for the cluster and then add both the master and worker availability sets
        // to the proximity group.  This ensures the shortest possible network latency
        // between all of the cluster nodes but with the increased chance that Azure
        // won't be able to satisfy the deployment constraints.  The user can disable
        // this placement groups via [AzureOptions.DisableProximityPlacement].
        //
        // The VNET will be configured using the cluster definitions's [NetworkOptions]
        // and the node IP addresses will be automatically assigned by default
        // but this can be customized via the cluster definition when necessary.
        // The load balancer will be created using a public IP address with
        // NAT rules forwarding network traffic into the cluster.  These rules
        // are controlled by [NetworkOptions.IngressRoutes] in the cluster
        // definition.  The target nodes in the cluster are indicated by the
        // presence of a [neonkube.io/node.ingress=true] label which can be
        // set explicitly for each node or assigned via a [NetworkOptions.IngressNodeSelector]
        // label selector.  neonKUBE will use reasonable defaults when necessary.
        //
        // Azure load balancers will be configured with two security rules:
        // [public] and [private].  By default, these rules will allow traffic
        // from any IP address with the [public] rule being applied to all
        // of the ingress routes and the [private] rules being applied to
        // temporary node-specific SSH rules used for cluster setup and maintainence.
        // You may wish to constrain these to specific IP addresses or subnets
        // for better security.
        //
        // VMs are currently based on the Ubuntu-20.04 Server image provided by 
        // published to the marketplace by Canonical.  They publish Gen1 and Gen2
        // images.  I believe Gen2 images will work on Azure Gen1 & Gen2 instances
        // so our images will be Gen2 based as well.
        //
        // This hosting manager will support creating VMs from the base Canonical
        // image as well as from custom images published to the marketplace by
        // neonFORGE.  The custom images will be preprovisioned with all of the
        // software required, making cluster setup much faster and reliable.  The
        // Canonical based images will need lots of configuration before they can
        // be added to a cluster.  Note that the neonFORGE images are actually
        // created by starting with a Canonical image and doing most of a cluster
        // setup on that image, so we'll continue supporting the raw Canonical
        // images.
        //
        // We're also going to be supporting two different ways of managing the
        // cluster deployment process.  The first approach will be to continue
        // controlling the process from a client application: [neon-cli] or
        // neonDESKTOP using SSH to connect to the nodes via temporary NAT
        // routes through the public load balancer.  neonKUBE clusters reserve
        // 1000 inbound ports (the actual range is configurable in the cluster
        // definition [CloudOptions]) and we'll automatically create NAT rule
        // for each node that routes external SSH traffic to the node.
        //
        // The second approach is to handle cluster setup from within the cloud
        // itself.  We're probably going to defer doing until after we go public
        // with neonCLOUD.  There's two ways of accomplising this: one is to
        // deploy a very small temporary VM within the customer's Azure subscription
        // that lives within the cluster VNET and coordinates things from there.
        // The other way is to is to manage VM setup from a neonCLOUD service,
        // probably using temporary load balancer SSH routes to access specific
        // nodes.  Note that this neonCLOUD service could run anywhere; it is
        // not restricted to running withing the same region as the customer
        // cluster.
        // 
        // Node instance and disk types and sizes are specified by the 
        // [NodeDefinition.Azure] property.  Instance types are specified
        // using standard Azure names, disk type is an enum and disk sizes
        // are specified via strings including optional [ByteUnits].  Provisioning
        // will need to verify that the requested instance and drive types are
        // actually available in the target Azure region and will also need
        // to map the disk size specified by the user to the closest matching
        // Azure disk size.

        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Relates hive node information with Azure VM information.
        /// </summary>
        private class AzureNode
        {
            private AzureHostingManager hostingManager;

            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="node">The associated node proxy.</param>
            /// <param name="hostingManager">The parent hosting manager.</param>
            public AzureNode(SshProxy<NodeDefinition> node, AzureHostingManager hostingManager)
            {
                Covenant.Requires<ArgumentNullException>(hostingManager != null, nameof(hostingManager));

                this.Node           = node;
                this.hostingManager = hostingManager;
            }

            /// <summary>
            /// Returns the associated node proxy.
            /// </summary>
            public SshProxy<NodeDefinition> Node { get; private set; }

            /// <summary>
            /// Returns the node name.
            /// </summary>
            public string Name => hostingManager.namePrefix + Node.Name;

            /// <summary>
            /// The associated Azure VM.
            /// </summary>
            public IVirtualMachine Vm { get; set; }

            /// <summary>
            /// The node's network interface.
            /// </summary>
            public INetworkInterface Nic { get; set; }

            /// <summary>
            /// The SSH port to be used to connect to the node via SSH while provisioning
            /// or managing the cluster.
            /// </summary>
            public int PublicSshPort { get; set; } = NetworkPorts.SSH;

            /// <summary>
            /// Returns the Azure name for the temporary NAT rule mapping the
            /// cluster's frontend load balancer port to the SSH port for this 
            /// node.
            /// </summary>
            public string SshNatRuleName
            {
                get { return $"neon-ssh-tcp-{Node.Name}"; }
            }

            /// <summary>
            /// Returns <c>true</c> if the node is a master.
            /// </summary>
            public bool IsMaster
            {
                get { return Node.Metadata.Role == NodeRole.Master; }
            }

            /// <summary>
            /// Returns <c>true</c> if the node is a worker.
            /// </summary>
            public bool IsWorker
            {
                get { return Node.Metadata.Role == NodeRole.Worker; }
            }
        }

        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Ensures that the assembly hosting this hosting manager is loaded.
        /// </summary>
        public static void Load()
        {
            // We don't have to do anything here because the assembly is loaded
            // as a byproduct of calling this method.
        }

        //---------------------------------------------------------------------
        // Instance members

        private ClusterProxy                    cluster;
        private string                          clusterName;
        private AzureCredentials                azureCredentials;
        private IAzure                          azure;
        private string                          region;
        private string                          resourceGroup;
        private KubeSetupInfo                   setupInfo;
        private HostingOptions                  hostingOptions;
        private AzureOptions                    azureOptions;
        private NetworkOptions                  networkOptions;
        private Dictionary<string, AzureNode>   nodeDictionary;

        // Azure requires that the various components that need to be provisioned
        // for the cluster have names.  We're going to generate these in the constructor.
        // Top level component names will be prefixed by
        //
        //      neon-<cluster-name>-
        //
        // to avoid conflicts with other clusters or things deployed to the same resource
        // group.  For example if there's already a cluster in the same resource group,
        // we wouldn't want to node names like "master-0" to conflict across multiple 
        // clusters.

        private string                          namePrefix;
        private string                          publicIpName;
        private string                          vnetName;
        private string                          subnetName;
        private string                          masterAvailabilitySetName;
        private string                          workerAvailabilitySetName;
        private string                          proximityPlacementGroupName;
        private string                          loadbalancerName;
        private string                          loadbalancerFrontendName;
        private string                          loadbalancerBackendName;
        private string                          loadbalancerProbeName;
        private string                          publicNetworkSecurityGroupName;
        private string                          privateNetworkSecurityGroupName;
        private string                          outboudNetworkSecurityGroupName;

        // These fields hold various Azure components while provisioning is in progress.

        private IPublicIPAddress                publicIp;
        private INetwork                        vnet;
        private ILoadBalancer                   loadBalancer;
        private IAvailabilitySet                masterAvailabilitySet;
        private IAvailabilitySet                workerAvailabilitySet;
        private INetworkSecurityGroup           publicNetworkSecurityGroup;
        private INetworkSecurityGroup           privateNetworkSecurityGroup;
        private INetworkSecurityGroup           outboundNetworkSecurityGroup;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="cluster">The cluster being managed.</param>
        /// <param name="setupInfo">Specifies the cluster setup information.</param>
        /// <param name="logFolder">
        /// The folder where log files are to be written, otherwise or <c>null</c> or 
        /// empty if logging is disabled.
        /// </param>
        public AzureHostingManager(ClusterProxy cluster, KubeSetupInfo setupInfo, string logFolder = null)
        {
            Covenant.Requires<ArgumentNullException>(cluster != null, nameof(cluster));
            Covenant.Requires<ArgumentNullException>(setupInfo != null, nameof(setupInfo));

            cluster.HostingManager = this;

            this.cluster                         = cluster;
            this.clusterName                     = cluster.Name;
            this.resourceGroup                   = cluster.Definition.Hosting.Azure.ResourceGroup ?? $"neon-{clusterName}";
            this.setupInfo                       = setupInfo;
            this.hostingOptions                  = cluster.Definition.Hosting;
            this.azureOptions                    = cluster.Definition.Hosting.Azure;
            this.region                          = azureOptions.Region;
            this.networkOptions                  = cluster.Definition.Network;

            // Initialize the component names as they will be deployed to Azure.

            this.namePrefix                      = $"neon-{clusterName}-";
            this.publicIpName                    = namePrefix + "public-ip";
            this.vnetName                        = namePrefix + "vnet";
            this.masterAvailabilitySetName       = namePrefix + "master-availability-set";
            this.workerAvailabilitySetName       = namePrefix + "worker-availability-set";
            this.proximityPlacementGroupName     = namePrefix + "proxmity-group";
            this.loadbalancerName                = namePrefix + "load-balancer";

            // These names are relative to another component, so they don't require a prefix.

            this.loadbalancerFrontendName        = "frontend";
            this.loadbalancerBackendName         = "backend;";
            this.loadbalancerProbeName           = "probe";
            this.publicNetworkSecurityGroupName  = "neon-public";
            this.privateNetworkSecurityGroupName = "neon-private";
            this.outboudNetworkSecurityGroupName = "neon-outbound";

            // Initialize the node mapping dictionary and also ensure
            // that each node has Azure reasonable Azure node options.

            this.nodeDictionary = new Dictionary<string, AzureNode>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var node in cluster.Nodes)
            {
                nodeDictionary.Add(node.Name, new AzureNode(node, this));

                if (node.Metadata.Azure == null)
                {
                    // Initialize reasonable defaults.

                    node.Metadata.Azure = new AzureNodeOptions();
                }
            }

            // This identifies the cluster manager instance with the cluster proxy
            // so that the proxy can have the hosting manager perform some operations
            // like managing the SSH port mappings on the load balancer.

            cluster.HostingManager = this;
        }

        /// <inheritdoc/>
        public override void Dispose(bool disposing)
        {
            // The Azure connection class doesn't implement [IDispose]
            // so we don't have much to do here.

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        /// <inheritdoc/>
        public override void Validate(ClusterDefinition clusterDefinition)
        {
            // Ensure that any explicit node IP address assignments are located
            // within the nodes subnet and do not conflict with any of the addresses
            // reserved by the cloud provider or neonKUBE.  We're also going to 
            // require that the nodes subnet have at least 256 addresses.

            var nodeSubnet = NetworkCidr.Parse(clusterDefinition.Network.NodeSubnet);

            if (nodeSubnet.AddressCount < 256)
            {
                throw new ClusterDefinitionException($"[{nameof(clusterDefinition.Network.NodeSubnet)}={clusterDefinition.Network.NodeSubnet}] is too small.  Cloud subnets must include at least 256 addresses (at least a /24 network).");
            }

            const int reservedCount = KubeConst.CloudVNetStartReservedIPs + KubeConst.CloudVNetEndReservedIPs;

            if (clusterDefinition.Nodes.Count() > nodeSubnet.AddressCount - reservedCount)
            {
                throw new ClusterDefinitionException($"The cluster includes [{clusterDefinition.Nodes.Count()}] which will not fit within the [{nameof(clusterDefinition.Network.NodeSubnet)}={clusterDefinition.Network.NodeSubnet}] after accounting for [{reservedCount}] reserved addresses.");
            }

            var firstValidAddressUint = NetHelper.AddressToUint(nodeSubnet.FirstAddress) + KubeConst.CloudVNetStartReservedIPs;
            var firstValidAddress     = NetHelper.UintToAddress(firstValidAddressUint);
            var lastValidAddressUint  = NetHelper.AddressToUint(nodeSubnet.LastAddress) - KubeConst.CloudVNetEndReservedIPs;
            var lastValidAddress      = NetHelper.UintToAddress(lastValidAddressUint);

            foreach (var node in clusterDefinition.SortedNodes.OrderBy(node => node.Name))
            {
                if (string.IsNullOrEmpty(node.Address))
                {
                    // Ignore nodes with unassigned addresses.

                    continue;
                }

                var address = IPAddress.Parse(node.Address);

                if (!nodeSubnet.Contains(address))
                {
                    throw new ClusterDefinitionException($"Node [{node.Name}] is assigned [{node.Address}={node.Address}] which is outside of [{nameof(clusterDefinition.Network.NodeSubnet)}={clusterDefinition.Network.NodeSubnet}].");
                }

                var addressUint = NetHelper.AddressToUint(address);

                if (addressUint < firstValidAddressUint)
                {
                    throw new ClusterDefinitionException($"Node [{node.Name}] defines IP address [{node.Address}={node.Address}] which is reserved.  The first valid node address [{nameof(clusterDefinition.Network.NodeSubnet)}={clusterDefinition.Network.NodeSubnet}] is [{firstValidAddress}].");
                }

                if (addressUint > lastValidAddressUint)
                {
                    throw new ClusterDefinitionException($"Node [{node.Name}] defines IP address [{node.Address}={node.Address}] which is reserved.  The last valid node address [{nameof(clusterDefinition.Network.NodeSubnet)}={clusterDefinition.Network.NodeSubnet}] is [{lastValidAddress}].");
                }
            }

            //-----------------------------------------------------------------
            // Automatically assign IP unused IP addresses within the subnet to nodes that 
            // were not explicitly assigned an address in the cluster definition.

            // Add any explicitly assigned addresses to a HashSet so we won't reuse any.

            var assignedAddresses = new HashSet<uint>();

            foreach (var node in clusterDefinition.SortedNodes)
            {
                if (string.IsNullOrEmpty(node.Address))
                {
                    continue;
                }

                var address     = IPAddress.Parse(node.Address);
                var addressUint = NetHelper.AddressToUint(address);

                if (!assignedAddresses.Contains(addressUint))
                {
                    assignedAddresses.Add(addressUint);
                }
            }

            // Assign master node addresses first so these will tend to appear first
            // in the subnet.

            foreach (var node in clusterDefinition.SortedNodes.Where(node => node.IsMaster))
            {
                if (!string.IsNullOrEmpty(node.Address))
                {
                    continue;
                }

                for (var addressUint = firstValidAddressUint; addressUint <= lastValidAddressUint; addressUint++)
                {
                    if (!assignedAddresses.Contains(addressUint))
                    {
                        node.Address = NetHelper.UintToAddress(addressUint).ToString();

                        assignedAddresses.Add(addressUint);
                        break;
                    }
                }
            }

            // Now assign the worker node addresses, so these will tend to appear
            // after the masters in the subnet.

            foreach (var node in clusterDefinition.SortedNodes.Where(node => node.IsWorker))
            {
                if (!string.IsNullOrEmpty(node.Address))
                {
                    continue;
                }

                for (var addressUint = firstValidAddressUint; addressUint <= lastValidAddressUint; addressUint++)
                {
                    if (!assignedAddresses.Contains(addressUint))
                    {
                        node.Address = NetHelper.UintToAddress(addressUint).ToString();

                        assignedAddresses.Add(addressUint);
                        break;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override bool Provision(bool force, string secureSshPassword, string orgSshPassword = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(secureSshPassword));

            var operation  = $"Provisioning [{cluster.Definition.Name}] on Azure [{region}/{resourceGroup}]";
            var controller = new SetupController<NodeDefinition>(operation, cluster.Nodes)
            {
                ShowStatus     = this.ShowStatus,
                ShowNodeStatus = false,
                MaxParallel    = this.MaxParallel
            };

            controller.AddGlobalStep("connecting Azure", () => AzureConnect());
            controller.AddGlobalStep("region availablity", () => VerifyRegionAndVmSizes());
            controller.AddGlobalStep("resource group", () => ResourceGroup());
            controller.AddGlobalStep("availability sets", () => AvailabilitySets());

            if (!controller.Run(leaveNodesConnected: false))
            {
                Console.WriteLine("*** One or more Azure provisioning steps failed.");
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override (string Address, int Port) GetSshEndpoint(string nodeName)
        {
            return (Address: cluster.GetNode(nodeName).Address.ToString(), Port: NetworkPorts.SSH);
        }

        /// <inheritdoc/>
        public override bool RequiresAdminPrivileges => false;

        /// <summary>
        /// Connects to Azure if we're not already connected.
        /// </summary>
        private void AzureConnect()
        {
            if (azure != null)
            {
                return; // Already connected.
            }

            var environment = AzureEnvironment.AzureGlobalCloud;

            if (azureOptions.Environment != null)
            {
                switch (azureOptions.Environment.Name)
                {
                    case AzureCloudEnvironments.GlobalCloud:

                        environment = AzureEnvironment.AzureGlobalCloud;
                        break;

                    case AzureCloudEnvironments.ChinaCloud:

                        environment = AzureEnvironment.AzureChinaCloud;
                        break;

                    case AzureCloudEnvironments.GermanCloud:

                        environment = AzureEnvironment.AzureGermanCloud;
                        break;

                    case AzureCloudEnvironments.USGovernment:

                        environment = AzureEnvironment.AzureUSGovernment;
                        break;

                    case AzureCloudEnvironments.Custom:

                        environment = new AzureEnvironment()
                        {
                            AuthenticationEndpoint  = azureOptions.Environment.AuthenticationEndpoint,
                            GraphEndpoint           = azureOptions.Environment.GraphEndpoint,
                            ManagementEndpoint      = azureOptions.Environment.ManagementEnpoint,
                            ResourceManagerEndpoint = azureOptions.Environment.ResourceManagerEndpoint
                        };
                        break;

                    default:

                        throw new NotImplementedException($"Azure environment [{azureOptions.Environment.Name}] is not currently supported.");
                }
            }

            azureCredentials =
                new AzureCredentials(
                    new ServicePrincipalLoginInformation()
                    {
                        ClientId     = azureOptions.AppId,
                        ClientSecret = azureOptions.AppPassword
                    },
                azureOptions.TenantId,
                environment);

            azure = Azure.Configure()
                .Authenticate(azureCredentials)
                .WithSubscription(azureOptions.SubscriptionId);
        }

        /// <summary>
        /// Verify that the requested Azure region exists, supports the requested VM sizes,
        /// and that VMs for nodes that specify UltraSSD actually support UltraSSD.  We'll also
        /// verify that the requested VMs have the minimum required number or cores and RAM.
        /// </summary>
        private void VerifyRegionAndVmSizes()
        {
            var ultraSSD     = cluster.Nodes.Any(node => node.Metadata.Azure.StorageType == AzureStorageTypes.UltraSSD);
            var region       = cluster.Definition.Hosting.Azure.Region;
            var vmSizes      = azure.VirtualMachines.Sizes.ListByRegion(region);
            var nameToVmSize = new Dictionary<string, IVirtualMachineSize>(StringComparer.InvariantCultureIgnoreCase);
            var nameToVmSku  = new Dictionary<string, IComputeSku>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var vmSize in azure.VirtualMachines.Sizes.ListByRegion(region))
            {
                nameToVmSize[vmSize.Name] =  vmSize;
            }

            foreach (var vmSku in azure.ComputeSkus.ListByRegion(region))
            {
                nameToVmSku[vmSku.Name.Value] = vmSku;
            }

            foreach (var node in cluster.Nodes)
            {
                var vmSizeName = node.Metadata.Azure.VmSize;

                if (!nameToVmSize.TryGetValue(vmSizeName, out var vmSize))
                {
                    throw new KubeException($"Node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}].  This is not available in the [{region}] Azure region.");
                }

                if (!nameToVmSku.TryGetValue(vmSizeName, out var vmSku))
                {
                    // This should never happen, right?

                    throw new KubeException($"Node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}].  This is not available in the [{region}] Azure region.");
                }

                switch (node.Metadata.Role)
                {
                    case NodeRole.Master:

                        if (vmSize.NumberOfCores < KubeConst.MinMasterCores)
                        {
                            throw new KubeException($"Master node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}] with [Cores={vmSize.NumberOfCores} MiB] which is lower than the required [{KubeConst.MinMasterCores}] cores.]");
                        }

                        if (vmSize.MemoryInMB < KubeConst.MinMasterRamMiB)
                        {
                            throw new KubeException($"Master node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}] with [RAM={vmSize.MemoryInMB} MiB] which is lower than the required [{KubeConst.MinMasterRamMiB} MiB].]");
                        }

                        if (vmSize.MaxDataDiskCount < 1)
                        {
                            throw new KubeException($"Master node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}] that supports up to [{vmSize.MaxDataDiskCount}] disks.  A minimum of [1] drive is required.");
                        }
                        break;

                    case NodeRole.Worker:

                        if (vmSize.NumberOfCores < KubeConst.MinWorkerCores)
                        {
                            throw new KubeException($"Worker node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}] with [Cores={vmSize.NumberOfCores} MiB] which is lower than the required [{KubeConst.MinWorkerCores}] cores.]");
                        }

                        if (vmSize.MemoryInMB < KubeConst.MinWorkerRamMiB)
                        {
                            throw new KubeException($"Worker node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}] with [RAM={vmSize.MemoryInMB} MiB] which is lower than the required [{KubeConst.MinWorkerRamMiB} MiB].]");
                        }

                        if (vmSize.MaxDataDiskCount < 1)
                        {
                            throw new KubeException($"Worker node [{node.Name}] requests [{nameof(node.Metadata.Azure.VmSize)}={vmSizeName}] that supports up to [{vmSize.MaxDataDiskCount}] disks.  A minimum of [1] drive is required.");
                        }
                        break;

                    default:

                        throw new NotImplementedException();
                }

                if (node.Metadata.Azure.StorageType == AzureStorageTypes.UltraSSD)
                {
                    if (!vmSku.Capabilities.Any(Capability => Capability.Name == "UltraSSDAvailable" && Capability.Value == "False"))
                    {
                        throw new KubeException($"Node [{node.Name}] requests an UltraSSD disk.  This is not available in the [{region}] Azure region and/or the [{vmSize}] VM Size.");
                    }
                }
            }
        }

        /// <summary>
        /// Creates the cluster's resource group if it doesn't already exist.
        /// </summary>
        private void ResourceGroup()
        {
            if (azure.ResourceGroups.Contain(resourceGroup))
            {
                return;
            }

            azure.ResourceGroups
                .Define(resourceGroup)
                .WithRegion(region)
                .Create();
        }

        /// <summary>
        /// Creates an avilablity set for the master VMs an a separate one for the worker VMs.
        /// </summary>
        private void AvailabilitySets()
        {
            var existing = azure.AvailabilitySets.ListByResourceGroup(resourceGroup);

            masterAvailabilitySet = existing.FirstOrDefault(aset => aset.Name == masterAvailabilitySetName);
            workerAvailabilitySet = existing.FirstOrDefault(aset => aset.Name == workerAvailabilitySetName);

            if (azureOptions.DisableProximityPlacement)
            {
                if (masterAvailabilitySet == null)
                {
                    masterAvailabilitySet = (IAvailabilitySet)azure.AvailabilitySets.Define(masterAvailabilitySetName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithUpdateDomainCount(azureOptions.UpdateDomains)
                        .WithFaultDomainCount(azureOptions.FaultDomains);
                }

                if (workerAvailabilitySet == null)
                {
                    workerAvailabilitySet = (IAvailabilitySet)azure.AvailabilitySets.Define(workerAvailabilitySetName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithUpdateDomainCount(azureOptions.UpdateDomains)
                        .WithFaultDomainCount(azureOptions.FaultDomains);
                }
            }
            else
            {
                if (masterAvailabilitySet == null)
                {
                    masterAvailabilitySet = (IAvailabilitySet)azure.AvailabilitySets.Define(masterAvailabilitySetName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithProximityPlacementGroup(proximityPlacementGroupName)
                        .WithUpdateDomainCount(azureOptions.UpdateDomains)
                        .WithFaultDomainCount(azureOptions.FaultDomains);
                }

                if (workerAvailabilitySet == null)
                {
                    workerAvailabilitySet = (IAvailabilitySet)azure.AvailabilitySets.Define(workerAvailabilitySetName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithProximityPlacementGroup(proximityPlacementGroupName)
                        .WithUpdateDomainCount(azureOptions.UpdateDomains)
                        .WithFaultDomainCount(azureOptions.FaultDomains);
                }
            }
        }
    }
}

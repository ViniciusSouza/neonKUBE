﻿//-----------------------------------------------------------------------------
// FILE:	    KubeSetup.Operations.cs
// CONTRIBUTOR: Jeff Lill
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Helm.Helm;
using k8s;
using k8s.Models;
using Microsoft.Rest;
using Minio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;

using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Postgres;
using Neon.Retry;
using Neon.SSH;
using Neon.Tasks;
using Neon.Net;
using Microsoft.IdentityModel.Tokens;

namespace Neon.Kube
{
    public static partial class KubeSetup
    {
        /// <summary>
        /// Configures a local HAProxy container that makes the Kubernetes Etc
        /// cluster highly available.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="node">The node where the operation will be performed.</param>
        public static void SetupEtcdHaProxy(ISetupController controller, NodeSshProxy<NodeDefinition> node)
        {
            Covenant.Requires<ArgumentException>(controller != null, nameof(controller));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);

            controller.LogProgress(node, verb: "configure", message: "etcd ha");

            var sbHaProxyConfig = new StringBuilder();

            sbHaProxyConfig.Append(
$@"global
    daemon
    log stdout  format raw  local0  info
    maxconn 32000

defaults
    balance                 roundrobin
    retries                 2
    http-reuse              safe
    timeout connect         5000
    timeout client          50000
    timeout server          50000
    timeout check           5000
    timeout http-keep-alive 500

frontend kubernetes_masters
    bind                    *:6442
    mode                    tcp
    log                     global
    option                  tcplog
    default_backend         kubernetes_masters_backend

frontend harbor_http
    bind                    *:80
    mode                    http
    log                     global
    option                  httplog
    default_backend         harbor_backend_http

frontend harbor
    bind                    *:443
    mode                    tcp
    log                     global
    option                  tcplog
    default_backend         harbor_backend

backend kubernetes_masters_backend
    mode                    tcp
    balance                 roundrobin");

            foreach (var master in cluster.Masters)
            {
                sbHaProxyConfig.Append(
$@"
    server {master.Name}         {master.Address}:{KubeNodePorts.KubeApiServer}");
            }

            sbHaProxyConfig.Append(
$@"
backend harbor_backend_http
    mode                    http
    balance                 roundrobin");

            foreach (var n in cluster.Nodes.Where(n => n.Metadata.Labels.Istio))
            {
                sbHaProxyConfig.Append(
$@"
    server {n.Name}         {n.Address}:{KubeNodePorts.IstioIngressHttp}");
            }

            sbHaProxyConfig.Append(
$@"
backend harbor_backend
    mode                    tcp
    balance                 roundrobin");

            foreach (var n in cluster.Nodes.Where(n => n.Metadata.Labels.Istio))
            {
                sbHaProxyConfig.Append(
$@"
    server {n.Name}         {n.Address}:{KubeNodePorts.IstioIngressHttps}");
            }

            node.UploadText("/etc/neonkube/neon-etcd-proxy.cfg", sbHaProxyConfig);

            var sbHaProxyPod = new StringBuilder();

            sbHaProxyPod.Append(
$@"
apiVersion: v1
kind: Pod
metadata:
  name: neon-etcd-proxy
  namespace: kube-system
  labels:
    app: neon-etcd-proxy
    role: neon-etcd-proxy
    release: neon-etcd-proxy
spec:
  volumes:
   - name: neon-etcd-proxy-config
     hostPath:
       path: /etc/neonkube/neon-etcd-proxy.cfg
       type: File
  hostNetwork: true
  containers:
    - name: web
      image: {KubeConst.LocalClusterRegistry}/haproxy:{KubeVersions.Haproxy}
      volumeMounts:
        - name: neon-etcd-proxy-config
          mountPath: /etc/haproxy/haproxy.cfg
      ports:
        - name: k8s-masters
          containerPort: 6442
          protocol: TCP
");
            node.UploadText("/etc/kubernetes/manifests/neon-etcd-proxy.yaml", sbHaProxyPod, permissions: "600", owner: "root:root");
        }

        /// <summary>
        /// Adds the Kubernetes node labels.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The first master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task LabelNodesAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s     = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/label-nodes",
                async () =>
                {
                    controller.LogProgress(master, verb: "label", message: "nodes");

                    try
                    {
                        var k8sNodes = (await k8s.ListNodeAsync()).Items;

                        foreach (var node in cluster.Nodes)
                        {
                            var k8sNode = k8sNodes.Where(n => n.Metadata.Name == node.Name).FirstOrDefault();

                            var patch = new V1Node()
                            {
                                Metadata = new V1ObjectMeta()
                                {
                                    Labels = k8sNode.Labels()
                                }
                            };

                            if (node.Metadata.IsWorker)
                            {
                                // Kubernetes doesn't set the role for worker nodes so we'll do that here.

                                patch.Metadata.Labels.Add("kubernetes.io/role", "worker");
                            }

                            patch.Metadata.Labels.Add(NodeLabels.LabelDatacenter, cluster.Definition.Datacenter.ToLowerInvariant());
                            patch.Metadata.Labels.Add(NodeLabels.LabelEnvironment, cluster.Definition.Environment.ToString().ToLowerInvariant());

                            foreach (var label in node.Metadata.Labels.All)
                            {
                                if (label.Value != null)
                                {
                                    patch.Metadata.Labels.Add(label.Key, label.Value.ToString());
                                }
                            }

                            await k8s.PatchNodeAsync(new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch), k8sNode.Metadata.Name);
                        }
                    }
                    finally
                    {
                        master.Status = string.Empty;
                    }

                    await Task.CompletedTask;
                });
        }

        /// <summary>
        /// Initializes the cluster on the first manager, then joins the remaining
        /// masters and workers to the cluster.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="maxParallel">
        /// The maximum number of operations on separate nodes to be performed in parallel.
        /// This defaults to <see cref="defaultMaxParallelNodes"/>.
        /// </param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task SetupClusterAsync(ISetupController controller, int maxParallel = defaultMaxParallelNodes)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentException>(maxParallel > 0, nameof(maxParallel));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var master        = cluster.FirstMaster;
            var debugMode     = controller.Get<bool>(KubeSetupProperty.DebugMode);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);

            cluster.ClearStatus();

            ConfigureKubernetes(controller, cluster.FirstMaster);
            ConfigureWorkstation(controller, master);

            ConnectCluster(controller);

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await RestartPodsAsync(controller, master);
            }

            await ConfigureMasterTaintsAsync(controller, master);
            await TaintNodesAsync(controller);
            await LabelNodesAsync(controller, master);
            await CreateNamespacesAsync(controller, master);
            await CreateRootUserAsync(controller, master);
            await InstallCalicoCniAsync(controller, master);
            await InstallMetricsServerAsync(controller, master);
            await InstallIstioAsync(controller, master);

            if (cluster.Definition.Nodes.Where(node => node.Labels.Metrics).Count() >= 3)
            {
                await InstallEtcdAsync(controller, master);
            }

            await InstallPrometheusAsync(controller, master);
            await InstallCertManagerAsync(controller, master);
            await InstallKubeDashboardAsync(controller, master);
            await InstallNodeProblemDetectorAsync(controller, master);
            await InstallOpenEbsAsync(controller, master);
            await InstallReloaderAsync(controller, master);
            await InstallSystemDbAsync(controller, master);
            await InstallRedisAsync(controller, master);
            await InstallSsoAsync(controller, master);
            await InstallKialiAsync(controller, master);

            await InstallMinioAsync(controller, master);
            await SetupGrafanaAsync(controller, master);
            await InstallHarborAsync(controller, master);
            await InstallMonitoringAsync(controller);

            // Install the cluster operator and Harbor.

            await InstallClusterOperatorAsync(controller, master);
        }

        /// <summary>
        /// Method to generate Kubernetes cluster configuration.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static string GenerateKubernetesClusterConfig(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var hostingEnvironment   = controller.Get<HostingEnvironment>(KubeSetupProperty.HostingEnvironment);
            var cluster              = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin         = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var readyToGoMode        = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var controlPlaneEndpoint = $"kubernetes-masters:6442";
            var sbCertSANs           = new StringBuilder();

            if (hostingEnvironment == HostingEnvironment.Wsl2)
            {
                // Tweak the API server endpoint for WSL2.

                controlPlaneEndpoint = $"{KubeConst.NeonDesktopWsl2BuiltInDistroName}:{KubeNodePorts.KubeApiServer}";
            }

            if (!string.IsNullOrEmpty(cluster.Definition.Kubernetes.ApiLoadBalancer))
            {
                controlPlaneEndpoint = cluster.Definition.Kubernetes.ApiLoadBalancer;

                var fields = cluster.Definition.Kubernetes.ApiLoadBalancer.Split(':');

                sbCertSANs.AppendLine($"  - \"{fields[0]}\"");
                sbCertSANs.AppendLine($"  - \"kubernetes-masters\"");
            }

            foreach (var node in cluster.Masters)
            {
                sbCertSANs.AppendLine($"  - \"{node.Address}\"");
                sbCertSANs.AppendLine($"  - \"{node.Name}\"");
            }

            if (cluster.Definition.IsDesktopCluster)
            {
                sbCertSANs.AppendLine($"  - \"{Dns.GetHostName()}\"");
                sbCertSANs.AppendLine($"  - \"{cluster.Definition.Name}\"");
            }

            var kubeletFailSwapOnLine = string.Empty;

            if (hostingEnvironment == HostingEnvironment.Wsl2)
            {
                // SWAP will be enabled by the default Microsoft WSL2 kernel which
                // will cause Kubernetes to complain because this isn't a supported
                // configuration.  We need to disable these error checks.

                kubeletFailSwapOnLine = "failSwapOn: false";
            }

            var clusterConfig = new StringBuilder();

            clusterConfig.AppendLine(
$@"
apiVersion: kubeadm.k8s.io/v1beta2
kind: ClusterConfiguration
clusterName: {cluster.Name}
kubernetesVersion: ""v{KubeVersions.Kubernetes}""
imageRepository: ""{KubeConst.LocalClusterRegistry}""
apiServer:
  extraArgs:
    bind-address: 0.0.0.0
    advertise-address: 0.0.0.0
    logging-format: json
    default-not-ready-toleration-seconds: ""30"" # default 300
    default-unreachable-toleration-seconds: ""30"" #default  300
    allow-privileged: ""true""
    api-audiences: api
    service-account-issuer: kubernetes.default.svc
    service-account-key-file: /etc/kubernetes/pki/sa.key
    service-account-signing-key-file: /etc/kubernetes/pki/sa.key
    oidc-issuer-url: https://sso.{cluster.Definition.Domain}
    oidc-client-id: kubernetes
    oidc-username-claim: email
    oidc-groups-claim: groups
    oidc-username-prefix: ""-""
    oidc-groups-prefix: """"
  certSANs:
{sbCertSANs}
controlPlaneEndpoint: ""{controlPlaneEndpoint}""
networking:
  podSubnet: ""{cluster.Definition.Network.PodSubnet}""
  serviceSubnet: ""{cluster.Definition.Network.ServiceSubnet}""
controllerManager:
  extraArgs:
    logging-format: json
    node-monitor-grace-period: 15s #default 40s
    node-monitor-period: 5s #default 5s
    pod-eviction-timeout: 30s #default 5m0s
scheduler:
  extraArgs:
    logging-format: json");

            clusterConfig.AppendLine($@"
---
apiVersion: kubelet.config.k8s.io/v1beta1
kind: KubeletConfiguration
logging:
  format: json
nodeStatusReportFrequency: 4s
volumePluginDir: /var/lib/kubelet/volume-plugins
cgroupDriver: systemd
runtimeRequestTimeout: 5m
{kubeletFailSwapOnLine}
");

            var kubeProxyMode = "ipvs";

            clusterConfig.AppendLine($@"
---
apiVersion: kubeproxy.config.k8s.io/v1alpha1
kind: KubeProxyConfiguration
mode: {kubeProxyMode}");

            return clusterConfig.ToString();
        }

        /// <summary>
        /// Restart all pods in a cluster. This is used when updating CA certs.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task RestartPodsAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var hostingEnvironment = controller.Get<HostingEnvironment>(KubeSetupProperty.HostingEnvironment);
            var cluster            = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin       = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var readyToGoMode      = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var k8s                = GetK8sClient(controller);
            var numPods            = 0;

            await master.InvokeIdempotentAsync("ready-to-go/restart-pods",
                async () =>
                {
                    var pods = await k8s.ListPodForAllNamespacesAsync();

                    numPods = pods.Items.Count();

                    foreach (var p in pods.Items)
                    {
                        if (p.Name() == "kube-apiserver-neon-desktop")
                        {
                            continue;
                        }

                        await k8s.DeleteNamespacedPodAsync(p.Name(), p.Namespace(), gracePeriodSeconds: 0);
                    }
                });

            await master.InvokeIdempotentAsync("ready-to-go/wait-for-pods",
                async () =>
                {
                    await NeonHelper.WaitForAsync(
                            async () =>
                            {
                                try
                                {
                                    var pods = await k8s.ListPodForAllNamespacesAsync();

                                    return pods.Items.All(p => p.Status.Phase != "Pending") && pods.Items.Where(p => p.Namespace() == KubeNamespaces.NeonSystem).Count() > 1;
                                }
                                catch
                                {
                                    return false;
                                }
                            },
                            timeout:      TimeSpan.FromMinutes(10),
                            pollInterval: TimeSpan.FromMilliseconds(500));
                });
        }
        
        /// <summary>
        /// Basic Kubernetes cluster initialization.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static void ConfigureKubernetes(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var hostingEnvironment = controller.Get<HostingEnvironment>(KubeSetupProperty.HostingEnvironment);
            var cluster            = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin       = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var readyToGoMode      = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            
            master.InvokeIdempotent("setup/cluster-init",
                () =>
                {
                    //---------------------------------------------------------
                    // Initialize the cluster on the first master:

                    controller.LogProgress(master, verb: "create", message: "cluster");

                    // Initialize Kubernetes:

                    master.InvokeIdempotent("setup/kubernetes-init",
                        () =>
                        {
                            controller.LogProgress(master, verb: "initialize", message: "kubernetes");

                            // It's possible that a previous cluster initialization operation
                            // was interrupted.  This command resets the state.

                            master.SudoCommand("kubeadm reset --force");

                            SetupEtcdHaProxy(controller, master);

                            // CRI-O needs to be running and listening on its unix domain socket so that
                            // Kubelet can start and the cluster can be initialized via [kubeadm].  CRI-O
                            // takes perhaps 20-30 seconds to start and we've run into occassional trouble
                            // with cluster setup failures because CRI-O hadn't started listening on its
                            // socket in time.
                            //
                            // We're going to wait for the presence of the CRI-O socket here.

                            const string crioSocket = "/var/run/crio/crio.sock";

                            NeonHelper.WaitFor(
                                () =>
                                {
                                    var socketResponse  = master.SudoCommand("cat", new object[] { "/proc/net/unix" });

                                    return socketResponse.Success && socketResponse.OutputText.Contains(crioSocket);

                                },
                                pollInterval: TimeSpan.FromSeconds(0.5),
                                timeout: TimeSpan.FromSeconds(60));

                            // Configure the control plane's API server endpoint and initialize
                            // the certificate SAN names to include each master IP address as well
                            // as the HOSTNAME/ADDRESS of the API load balancer (if any).

                            controller.LogProgress(master, verb: "initialize", message: "cluster");

                            var clusterConfig = GenerateKubernetesClusterConfig(controller, master);

                            var kubeInitScript =
$@"
set -euo pipefail

systemctl enable kubelet.service
kubeadm init --config cluster.yaml --ignore-preflight-errors=DirAvailable--etc-kubernetes-manifests --cri-socket={crioSocket}
";
                            var response = master.SudoCommand(CommandBundle.FromScript(kubeInitScript).AddFile("cluster.yaml", clusterConfig.ToString()));

                            // Extract the cluster join command from the response.  We'll need this to join
                            // other nodes to the cluster.

                            var output = response.OutputText;
                            var pStart = output.IndexOf(joinCommandMarker, output.IndexOf(joinCommandMarker) + 1);

                            if (pStart == -1)
                            {
                                master.LogLine("START: [kubeadm init ...] response ============================================");

                                using (var reader = new StringReader(response.AllText))
                                {
                                    foreach (var line in reader.Lines())
                                    {
                                        master.LogLine(line);
                                    }
                                }

                                master.LogLine("END: [kubeadm init ...] response ==============================================");

                                throw new KubeException("Cannot locate the [kubeadm join ...] command in the [kubeadm init ...] response.");
                            }

                            var pEnd = output.Length;

                            if (pEnd == -1)
                            {
                                clusterLogin.SetupDetails.ClusterJoinCommand = Regex.Replace(output.Substring(pStart).Trim(), @"\t|\n|\r|\\", "");
                            }
                            else
                            {
                                clusterLogin.SetupDetails.ClusterJoinCommand = Regex.Replace(output.Substring(pStart, pEnd - pStart).Trim(), @"\t|\n|\r|\\", "");
                            }

                            clusterLogin.Save();

                            controller.LogProgress(master, verb: "created", message: "cluster");
                        });

                    master.InvokeIdempotent("setup/kubectl",
                        () =>
                        {
                            controller.LogProgress(master, verb: "configure", message: "kubectl");

                            // Edit the Kubernetes configuration file to rename the context:
                            //
                            //       CLUSTERNAME-admin@kubernetes --> root@CLUSTERNAME
                            //
                            // rename the user:
                            //
                            //      CLUSTERNAME-admin --> CLUSTERNAME-root 

                            var adminConfig = master.DownloadText("/etc/kubernetes/admin.conf");

                            adminConfig = adminConfig.Replace($"kubernetes-admin@{cluster.Definition.Name}", $"root@{cluster.Definition.Name}");
                            adminConfig = adminConfig.Replace("kubernetes-admin", $"root@{cluster.Definition.Name}");

                            master.UploadText("/etc/kubernetes/admin.conf", adminConfig, permissions: "600", owner: "root:root");
                        });

                    // Download the boot master files that will need to be provisioned on
                    // the remaining masters and may also be needed for other purposes
                    // (if we haven't already downloaded these).

                    if (clusterLogin.SetupDetails.MasterFiles != null)
                    {
                        clusterLogin.SetupDetails.MasterFiles = new Dictionary<string, KubeFileDetails>();
                    }

                    if (clusterLogin.SetupDetails.MasterFiles.Count == 0)
                    {
                        // I'm hardcoding the permissions and owner here.  It would be nice to
                        // scrape this from the source files in the future but it's not worth
                        // the bother at this point.

                        var files = new RemoteFile[]
                        {
                            new RemoteFile("/etc/kubernetes/admin.conf", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.crt", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.pub", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.key", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.key", "600", "root:root"),
                        };

                        foreach (var file in files)
                        {
                            var text = master.DownloadText(file.Path);

                            clusterLogin.SetupDetails.MasterFiles[file.Path] = new KubeFileDetails(text, permissions: file.Permissions, owner: file.Owner);
                        }
                    }

                    // Persist the cluster join command and downloaded master files.

                    clusterLogin.Save();

                    //---------------------------------------------------------
                    // Join the remaining masters to the cluster:

                    foreach (var master in cluster.Masters.Where(node => node != master))
                    {
                        try
                        {
                            master.InvokeIdempotent("setup/kubectl",
                                () =>
                                {
                                    controller.LogProgress(master, verb: "setup", message: "kubectl");

                                    // It's possible that a previous cluster join operation
                                    // was interrupted.  This command resets the state.

                                    master.SudoCommand("kubeadm reset --force");

                                    // The other (non-boot) masters need files downloaded from the boot master.

                                    controller.LogProgress(master, verb: "upload", message: "master files");

                                    foreach (var file in clusterLogin.SetupDetails.MasterFiles)
                                    {
                                        master.UploadText(file.Key, file.Value.Text, permissions: file.Value.Permissions, owner: file.Value.Owner);
                                    }

                                    // Join the cluster:

                                    master.InvokeIdempotent("setup/master-join",
                                        () =>
                                        {
                                            controller.LogProgress(master, verb: "join", message: "master to cluster");

                                            SetupEtcdHaProxy(controller, master);

                                            var joined = false;

                                            controller.LogProgress(master, verb: "join", message: "as master");

                                            master.SudoCommand("podman run",
                                                   "--name=neon-etcd-proxy",
                                                   "--detach",
                                                   "--restart=always",
                                                   "-v=/etc/neonkube/neon-etcd-proxy.cfg:/etc/haproxy/haproxy.cfg",
                                                   "--network=host",
                                                   "--log-driver=k8s-file",
                                                   $"{KubeConst.LocalClusterRegistry}/haproxy:{KubeVersions.Haproxy}"
                                               );

                                            for (int attempt = 0; attempt < maxJoinAttempts; attempt++)
                                            {
                                                var response = master.SudoCommand(clusterLogin.SetupDetails.ClusterJoinCommand + " --control-plane --ignore-preflight-errors=DirAvailable--etc-kubernetes-manifests", RunOptions.Defaults & ~RunOptions.FaultOnError);

                                                if (response.Success)
                                                {
                                                    joined = true;
                                                    break;
                                                }

                                                Thread.Sleep(joinRetryDelay);
                                            }

                                            if (!joined)
                                            {
                                                throw new Exception($"Unable to join node [{master.Name}] to the after [{maxJoinAttempts}] attempts.");
                                            }

                                            master.SudoCommand("docker kill neon-etcd-proxy");
                                            master.SudoCommand("docker rm neon-etcd-proxy");
                                        });
                                });
                        }
                        catch (Exception e)
                        {
                            master.Fault(NeonHelper.ExceptionError(e));
                            master.LogException(e);
                        }

                        controller.LogProgress(master, verb: "joined", message: "to cluster");
                    }

                    // Configure [kube-apiserver] on all the masters

                    foreach (var master in cluster.Masters)
                    {
                        try
                        {
                            master.InvokeIdempotent("setup/kubernetes-apiserver",
                                () =>
                                {
                                    controller.LogProgress(master, verb: "configure", message: "api server");

                                    master.SudoCommand(CommandBundle.FromScript(
@"#!/bin/bash

sed -i 's/.*--enable-admission-plugins=.*/    - --enable-admission-plugins=NamespaceLifecycle,LimitRanger,ServiceAccount,DefaultStorageClass,DefaultTolerationSeconds,MutatingAdmissionWebhook,ValidatingAdmissionWebhook,Priority,ResourceQuota/' /etc/kubernetes/manifests/kube-apiserver.yaml
"));
                                });
                        }
                        catch (Exception e)
                        {
                            master.Fault(NeonHelper.ExceptionError(e));
                            master.LogException(e);
                        }

                        master.Status = string.Empty;
                    }

                    //---------------------------------------------------------
                    // Join the remaining workers to the cluster:

                    var parallelOptions = new ParallelOptions()
                    {
                        MaxDegreeOfParallelism = defaultMaxParallelNodes
                    };

                    Parallel.ForEach(cluster.Workers, parallelOptions,
                        worker =>
                        {
                            try
                            {
                                worker.InvokeIdempotent("setup/worker-join",
                                    () =>
                                    {
                                        controller.LogProgress(worker, verb: "join", message: "worker to cluster");

                                        SetupEtcdHaProxy(controller, worker);

                                        var joined = false;

                                        controller.LogProgress(worker, verb: "join", message: "as worker");

                                        worker.SudoCommand("podman run",
                                            "--name=neon-etcd-proxy",
                                            "--detach",
                                            "--restart=always",
                                            "-v=/etc/neonkube/neon-etcd-proxy.cfg:/etc/haproxy/haproxy.cfg",
                                            "--network=host",
                                            "--log-driver=k8s-file",
                                            $"{KubeConst.LocalClusterRegistry}/haproxy:{KubeVersions.Haproxy}",
                                            RunOptions.FaultOnError);

                                        for (int attempt = 0; attempt < maxJoinAttempts; attempt++)
                                        {
                                            var response = worker.SudoCommand(clusterLogin.SetupDetails.ClusterJoinCommand + " --ignore-preflight-errors=DirAvailable--etc-kubernetes-manifests", RunOptions.Defaults & ~RunOptions.FaultOnError);

                                            if (response.Success)
                                            {
                                                joined = true;
                                                break;
                                            }

                                            Thread.Sleep(joinRetryDelay);
                                        }

                                        if (!joined)
                                        {
                                            throw new Exception($"Unable to join node [{worker.Name}] to the cluster after [{maxJoinAttempts}] attempts.");
                                        }

                                        worker.SudoCommand("docker kill neon-etcd-proxy");
                                        worker.SudoCommand("docker rm neon-etcd-proxy");
                                    });
                            }
                            catch (Exception e)
                            {
                                worker.Fault(NeonHelper.ExceptionError(e));
                                worker.LogException(e);
                            }

                            controller.LogProgress(worker, verb: "joined", message: "to cluster");
                        });
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                master.InvokeIdempotent("ready-to-go/renew-certs",
                () =>
                {
                    controller.LogProgress(master, verb: "ready-to-go", message: "renew kubectl certs");

                    var clusterConfig = GenerateKubernetesClusterConfig(controller, master);

                    var kubeInitScript =
$@"
set -euo pipefail

rm -rf /etc/kubernetes/pki/*
rm -f /etc/kubernetes/admin.conf
rm -f /etc/kubernetes/kubelet.conf
rm -f /etc/kubernetes/controller-manager.conf
rm -f /etc/kubernetes/scheduler.conf
kubeadm init --config cluster.yaml phase certs all
kubeadm init --config cluster.yaml phase kubeconfig all

set +e

until kubectl get pods
do
  sleep 0.5
done

for namespace in $(kubectl get ns --no-headers | awk '{{print $1}}'); do
    for token in $(kubectl get secrets --namespace ""$namespace"" --field-selector type=kubernetes.io/service-account-token -o name); do
        kubectl delete $token --namespace ""$namespace""
    done
done
";
                    var response = master.SudoCommand(CommandBundle.FromScript(kubeInitScript).AddFile("cluster.yaml", clusterConfig.ToString()));
                    var pods     = NeonHelper.JsonDeserialize<dynamic>(master.SudoCommand("crictl", "pods", "--namespace", "kube-system", "-o", "json").AllText);

                    foreach (dynamic p in pods.items)
                    {
                        master.SudoCommand("crictl", "stopp", p.id);
                        master.SudoCommand("crictl", "rmp", p.id);
                    }

                    master.SudoCommand("rm", "-f", "/var/lib/kubelet/pki/kubelet-client*");
                    master.SudoCommand("systemctl", "restart", "kubelet");

                    // Edit the Kubernetes configuration file to rename the context:
                    //
                    //       CLUSTERNAME-admin@kubernetes --> root@CLUSTERNAME
                    //
                    // rename the user:
                    //
                    //      CLUSTERNAME-admin --> CLUSTERNAME-root 

                    var adminConfig = master.DownloadText("/etc/kubernetes/admin.conf");

                    adminConfig = adminConfig.Replace($"kubernetes-admin@{cluster.Definition.Name}", $"root@{cluster.Definition.Name}");
                    adminConfig = adminConfig.Replace("kubernetes-admin", $"root@{cluster.Definition.Name}");

                    master.UploadText("/etc/kubernetes/admin.conf", adminConfig, permissions: "600", owner: "root:root");
                });

                master.InvokeIdempotent("ready-to-go/download-certs",
                () =>
                {
                    controller.LogProgress(master, verb: "readytogo", message: "renew kubectl certs");

                    // Download the boot master files that will need to be provisioned on
                    // the remaining masters and may also be needed for other purposes
                    // (if we haven't already downloaded these).

                    if (clusterLogin.SetupDetails.MasterFiles != null)
                    {
                        clusterLogin.SetupDetails.MasterFiles = new Dictionary<string, KubeFileDetails>();
                    }

                    if (clusterLogin.SetupDetails.MasterFiles.Count == 0)
                    {
                        // I'm hardcoding the permissions and owner here.  It would be nice to
                        // scrape this from the source files in the future but it's not worth
                        // the bother at this point.

                        var files = new RemoteFile[]
                        {
                            new RemoteFile("/etc/kubernetes/admin.conf", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.crt", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.pub", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.key", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.key", "600", "root:root"),
                        };


                    foreach (var file in files)
                    {
                        var text = master.DownloadText(file.Path);

                        clusterLogin.SetupDetails.MasterFiles[file.Path] = new KubeFileDetails(text, permissions: file.Permissions, owner: file.Owner);
                    }

                    clusterLogin.Save();
                    }
                });
            }
        }

        /// <summary>
        /// Configures the local workstation.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="firstMaster">The master node where the operation will be performed.</param>
        public static void ConfigureWorkstation(ISetupController controller, NodeSshProxy<NodeDefinition> firstMaster)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(firstMaster != null, nameof(firstMaster));

            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);

            firstMaster.InvokeIdempotent($"{(readyToGoMode == ReadyToGoMode.Setup ? "ready-to-go" : "setup")}/workstation",
                (Action)(() =>
                {
                    controller.LogProgress(firstMaster, verb: "configure", message: "workstation");

                    var cluster        = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
                    var clusterLogin   = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
                    var kubeConfigPath = KubeHelper.KubeConfigPath;

                    // Update kubeconfig.

                    // $todo(marcusbooyah):
                    //
                    // This is hardcoding the kubeconfig to point to the first master.  Issue 
                    // https://github.com/nforgeio/neonKUBE/issues/888 will fix this by adding a proxy
                    // to neonDESKTOP and load balancing requests across the k8s api servers.

                    var configText = clusterLogin.SetupDetails.MasterFiles["/etc/kubernetes/admin.conf"].Text;

                    configText = configText.Replace("kubernetes-masters", $"{cluster.Definition.Masters.FirstOrDefault().Address}");

                    if (!File.Exists(kubeConfigPath))
                    {
                        File.WriteAllText(kubeConfigPath, configText);
                    }
                    else
                    {
                        // The user already has an existing kubeconfig, so we need
                        // to merge in the new config.

                        var newConfig      = NeonHelper.YamlDeserialize<KubeConfig>(configText);
                        var existingConfig = KubeHelper.Config;

                        // Remove any existing user, context, and cluster with the same names.
                        // Note that we're assuming that there's only one of each in the config
                        // we downloaded from the cluster.

                        var newCluster      = newConfig.Clusters.Single();
                        var newContext      = newConfig.Contexts.Single();
                        var newUser         = newConfig.Users.Single();
                        var existingCluster = existingConfig.GetCluster(newCluster.Name);
                        var existingContext = existingConfig.GetContext(newContext.Name);
                        var existingUser    = existingConfig.GetUser(newUser.Name);

                        if (existingConfig != null)
                        {
                            existingConfig.Clusters.Remove(existingCluster);
                        }

                        if (existingContext != null)
                        {
                            existingConfig.Contexts.Remove(existingContext);
                        }

                        if (existingUser != null)
                        {
                            existingConfig.Users.Remove(existingUser);
                        }

                        existingConfig.Clusters.Add(newCluster);
                        existingConfig.Contexts.Add(newContext);
                        existingConfig.Users.Add(newUser);

                        existingConfig.CurrentContext = newContext.Name;

                        KubeHelper.SetConfig(existingConfig);
                    }

                    if (readyToGoMode == ReadyToGoMode.Setup)
                    {
                        var configFile = Environment.GetEnvironmentVariable("KUBECONFIG").Split(';').Where(variable => variable.Contains("config")).FirstOrDefault();

                        var k8sClient = new KubernetesWithRetry(KubernetesClientConfiguration.BuildConfigFromConfigFile(configFile, currentContext: cluster.KubeContext.Name));

                        k8sClient.RetryPolicy =
                            new ExponentialRetryPolicy(
                                transientDetector:
                                    exception =>
                                    {
                                        var exceptionType = exception.GetType();

                                            // Exceptions like this happen when a API server connection can't be established
                                            // because the server isn't running or ready.

                                            if (exceptionType == typeof(HttpRequestException) && exception.InnerException != null && exception.InnerException.GetType() == typeof(SocketException))
                                        {
                                            return true;
                                        }

                                        if (exceptionType == typeof(HttpOperationException) && ((HttpOperationException)exception).Response.StatusCode == HttpStatusCode.Forbidden)
                                        {
                                            return true;
                                        }

                                            // This might be another variant of the check just above.  This looks like an SSL negotiation problem.

                                            if (exceptionType == typeof(HttpRequestException) && exception.InnerException != null && exception.InnerException.GetType() == typeof(IOException))
                                        {
                                            return true;
                                        }

                                        return false;
                                    },
                                            maxAttempts:          int.MaxValue,
                                            initialRetryInterval: TimeSpan.FromSeconds(1),
                                            maxRetryInterval:     TimeSpan.FromSeconds(5),
                                            timeout:              TimeSpan.FromMinutes(5));

                        controller[KubeSetupProperty.K8sClient] = k8sClient;
                    }
                }));
        }

        /// <summary>
        /// Installs the Calico CNI.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallCalicoCniAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;
            var k8s     = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/cni",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "calico");

                    var values = new Dictionary<string, object>();

                    values.Add("images.organization", KubeConst.LocalClusterRegistry);

                    if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2)
                    {
                        values.Add($"neonDesktop", $"true");
                        values.Add($"kubernetes.service.host", $"neon-desktop");
                        values.Add($"kubernetes.service.port", KubeNodePorts.KubeApiServer);
                    }

                    await master.InstallHelmChartAsync(controller, "calico", releaseName: "calico", @namespace: KubeNamespaces.KubeSystem, values: values);

                    // Wait for Calico and CoreDNS pods to report that they're running.
                    // We're going to wait a maximum of 300 seconds.

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var pods = await k8s.ListPodForAllNamespacesAsync();

                            foreach (var pod in pods.Items)
                            {
                                if (pod.Status.Phase != "Running")
                                {
                                    if (pod.Metadata.Name.Contains("coredns") && pod.Status.Phase == "Pending")
                                    {
                                        master.SudoCommand("kubectl rollout restart --namespace kube-system deployment/coredns", RunOptions.LogOnErrorOnly);
                                    }

                                    await Task.Delay(5000);

                                    return false;
                                }
                            }

                            return true;
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpPollInterval);
                    
                    await master.InvokeIdempotentAsync("setup/dnsutils",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "setup", message: "dnsutils");

                            var pods = await k8s.CreateNamespacedPodAsync(
                                new V1Pod()
                                {
                                    Metadata = new V1ObjectMeta()
                                    {
                                        Name              = "dnsutils",
                                        NamespaceProperty = KubeNamespaces.NeonSystem
                                    },
                                    Spec = new V1PodSpec()
                                    {
                                        Containers = new List<V1Container>()
                                        {
                                            new V1Container()
                                            {
                                                Name            = "dnsutils",
                                                Image           = $"{KubeConst.LocalClusterRegistry}/kubernetes-e2e-test-images-dnsutils:{KubeVersions.DnsUtils}",
                                                Command         = new List<string>() {"sleep", "3600" },
                                                ImagePullPolicy = "IfNotPresent"
                                            }
                                        },
                                        RestartPolicy = "Always",
                                        Tolerations = new List<V1Toleration>()
                                        {
                                            { new V1Toleration() { Effect = "NoSchedule", OperatorProperty = "Exists" } },
                                            { new V1Toleration() { Effect = "NoExecute", OperatorProperty = "Exists" } }
                                        }
                                    }
                                }, 
                                KubeNamespaces.NeonSystem);
                        });

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var result = master.SudoCommand($"kubectl exec -n {KubeNamespaces.NeonSystem} -t dnsutils -- nslookup kubernetes.default", RunOptions.LogOutput);

                            if (result.Success)
                            {
                                return await Task.FromResult(true);
                            }
                            else
                            {
                                master.SudoCommand("kubectl rollout restart --namespace kube-system deployment/coredns", RunOptions.LogOnErrorOnly);
                                await Task.Delay(5000);
                                return await Task.FromResult(false);
                            }
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpPollInterval);

                    await k8s.DeleteNamespacedPodAsync("dnsutils", KubeNamespaces.NeonSystem);
                });
        }

        /// <summary>
        /// Uploads cluster related metadata to cluster nodes to <b>/etc/neonkube/metadata</b>
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="node">The target cluster node.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task ConfigureMetadataAsync(ISetupController controller, NodeSshProxy<NodeDefinition> node)
        {
            node.InvokeIdempotent("cluster-metadata",
                () =>
                {
                    node.UploadText(LinuxPath.Combine(KubeNodeFolders.Config, "metadata", "cluster-manifest.json"), NeonHelper.JsonSerialize(ClusterManifest, Formatting.Indented));
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Configures pods to be schedule on masters when enabled.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task ConfigureMasterTaintsAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;
            var k8s     = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/kubernetes-master-taints",
                async () =>
                {
                    controller.LogProgress(master, verb: "configure", message: "master taints");

                    // The [kubectl taint] command looks like it can return a non-zero exit code.
                    // We'll ignore this.

                    if (cluster.Definition.Kubernetes.AllowPodsOnMasters.GetValueOrDefault())
                    {
                        var nodes = new V1NodeList();

                        await NeonHelper.WaitForAsync(
                           async () =>
                           {
                               nodes = await k8s.ListNodeAsync(labelSelector: "node-role.kubernetes.io/master=");
                               return nodes.Items.All(n => n.Status.Conditions.Any(c => c.Type == "Ready" && c.Status == "True"));
                           },
                           timeout:      TimeSpan.FromMinutes(5),
                           pollInterval: TimeSpan.FromSeconds(5));

                        foreach (var master in nodes.Items)
                        {
                            if (master.Spec.Taints == null)
                            {
                                continue;
                            }

                            var patch = new V1Node()
                            {
                                Spec = new V1NodeSpec()
                                {
                                    Taints = master.Spec.Taints.Where(t => t.Key != "node-role.kubernetes.io/master").ToList()
                                }
                            };

                            await k8s.PatchNodeAsync(new V1Patch(patch, V1Patch.PatchType.StrategicMergePatch), master.Metadata.Name);
                        }
                    }
                });
        }

        /// <summary>
        /// Installs the Kubernetes Metrics Server service.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallMetricsServerAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;
            var k8s     = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/kubernetes-metrics-server",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "metrics-server");

                    var values = new Dictionary<string, object>();

                    values.Add("image.organization", KubeConst.LocalClusterRegistry);

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "metrics_server", releaseName: "metrics-server", @namespace: KubeNamespaces.KubeSystem, values: values);
                });

            await master.InvokeIdempotentAsync("setup/kubernetes-metrics-server-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "metrics-server");

                    await k8s.WaitForDeploymentAsync("kube-system", "metrics-server", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs Istio.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallIstioAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var k8s           = GetK8sClient(controller);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var ingressAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.IstioIngressGateway);
            var proxyAdvice   = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.IstioProxy);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);

            await master.InvokeIdempotentAsync("setup/ingress-namespace",
                async () =>
                {
                    await k8s.CreateNamespaceAsync(new V1Namespace()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = KubeNamespaces.NeonIngress
                        }
                    });
                });

            await master.InvokeIdempotentAsync("setup/ingress",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "ingress");

                    var values = new Dictionary<string, object>();

                    values.Add("cluster.name", clusterLogin.ClusterDefinition.Name);
                    values.Add("cluster.domain", clusterLogin.ClusterDefinition.Domain);

                    var i      = 0;
                    foreach (var rule in master.Cluster.Definition.Network.IngressRules)
                    {
                        values.Add($"nodePorts[{i}].name", $"{rule.Name}");
                        values.Add($"nodePorts[{i}].protocol", $"{rule.Protocol.ToString().ToUpper()}");
                        values.Add($"nodePorts[{i}].port", rule.ExternalPort);
                        values.Add($"nodePorts[{i}].targetPort", rule.TargetPort);
                        values.Add($"nodePorts[{i}].nodePort", rule.NodePort);
                        i++;
                    }

                    values.Add($"resources.ingress.limits.cpu", $"{ToSiString(ingressAdvice.PodCpuLimit)}");
                    values.Add($"resources.ingress.limits.memory", $"{ToSiString(ingressAdvice.PodMemoryLimit)}");
                    values.Add($"resources.ingress.requests.cpu", $"{ToSiString(ingressAdvice.PodCpuRequest)}");
                    values.Add($"resources.ingress.requests.memory", $"{ToSiString(ingressAdvice.PodMemoryRequest)}");

                    values.Add($"resources.proxy.limits.cpu", $"{ToSiString(proxyAdvice.PodCpuLimit)}");
                    values.Add($"resources.proxy.limits.memory", $"{ToSiString(proxyAdvice.PodMemoryLimit)}");
                    values.Add($"resources.proxy.requests.cpu", $"{ToSiString(proxyAdvice.PodCpuRequest)}");
                    values.Add($"resources.proxy.requests.memory", $"{ToSiString(proxyAdvice.PodMemoryRequest)}");

                    await master.InstallHelmChartAsync(controller, "istio", releaseName: "neon-ingress", @namespace: KubeNamespaces.NeonIngress, values: values);
                });

            await master.InvokeIdempotentAsync("setup/ingress-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "istio");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonIngress, "istio-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonIngress, "istiod", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDaemonsetAsync(KubeNamespaces.NeonIngress, "istio-ingressgateway", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDaemonsetAsync(KubeNamespaces.KubeSystem, "istio-cni-node", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                        });
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/neoncluster-gateway",
                async () =>
                {
                    var gateway      = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "gateways", "neoncluster-gateway")).ToObject<Gateway>();
                    var regexPattern = "[a-z0-9]+.neoncluster.io";
                    var servers      = new List<Server>();

                    foreach (var server in gateway.Spec.Servers)
                    {
                        var hosts = new List<string>();

                        foreach (var host in server.Hosts)
                        {
                            hosts.Add(Regex.Replace(host, regexPattern, cluster.Definition.Domain));
                        }
                        server.Hosts = hosts;
                    }

                    await k8s.ReplaceNamespacedCustomObjectAsync(gateway, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "gateways", "neoncluster-gateway");
                });
            }
        }

        /// <summary>
        /// Installs Cert Manager.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallCertManagerAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster            = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin       = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var k8s                = GetK8sClient(controller);
            var readyToGoMode      = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice      = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice      = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.CertManager);
            var ingressAdvice      = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.IstioIngressGateway);
            var proxyAdvice        = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.IstioProxy);
            var hostingEnvironment = controller.Get<HostingEnvironment>(KubeSetupProperty.HostingEnvironment);
            var clusterIp          = controller.Get<string>(KubeSetupProperty.ClusterIp);

            await master.InvokeIdempotentAsync("setup/cert-manager",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "cert-manager");

                    var values = new Dictionary<string, object>();

                    values.Add("image.organization", KubeConst.LocalClusterRegistry);
                    values.Add($"prometheus.servicemonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelIngress, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "cert_manager", releaseName: "cert-manager", @namespace: KubeNamespaces.NeonIngress, values: values);

                });


            await master.InvokeIdempotentAsync("setup/cert-manager-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "cert-manager");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonIngress, "cert-manager", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonIngress, "cert-manager-cainjector", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonIngress, "cert-manager-webhook", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                        });
                });

            await master.InvokeIdempotentAsync("setup/neon-acme",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "neon-acme");
                    
                    var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
                    var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
                    var values        = new Dictionary<string, object>();

                    values.Add("image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("image.tag", KubeVersions.NeonKubeContainerImageTag);
                    values.Add("cluster.name", clusterLogin.ClusterDefinition.Name);
                    values.Add("cluster.domain", clusterLogin.ClusterDefinition.Domain);

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelIngress, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "neon_acme", releaseName: "neon-acme", @namespace: KubeNamespaces.NeonIngress, values: values);

                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/cluster-domain",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "set cluster domain");

                        if (IPAddress.TryParse(clusterIp, out var ip))
                        {
                            using (var jsonClient = new JsonClient())
                            {
                                jsonClient.BaseAddress = new Uri(controller.Get<string>(KubeSetupProperty.HeadendUri));
                                clusterLogin.ClusterDefinition.Domain = await jsonClient.GetAsync<string>($"/cluster/domain?ipAddress={clusterIp}");
                                clusterLogin.Save();
                            }
                        }
                    });

                await master.InvokeIdempotentAsync("ready-to-go/cluster-cert",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "renew cluster cert");

                        var cert = ((JObject)await k8s.GetNamespacedCustomObjectAsync("cert-manager.io", "v1", KubeNamespaces.NeonIngress, "certificates", "neon-cluster-certificate")).ToObject<Certificate>();

                        cert.Spec.CommonName = clusterLogin.ClusterDefinition.Domain;
                        cert.Spec.DnsNames   = new List<string>()
                        {
                            $"{clusterLogin.ClusterDefinition.Domain}",
                            $"*.{clusterLogin.ClusterDefinition.Domain}"
                        };

                        await k8s.ReplaceNamespacedCustomObjectAsync(cert, "cert-manager.io", "v1", KubeNamespaces.NeonIngress, "certificates", "neon-cluster-certificate");

                        var harborCert = ((JObject)await k8s.GetNamespacedCustomObjectAsync("cert-manager.io", "v1", KubeNamespaces.NeonSystem, "certificates", "registry-harbor")).ToObject<Certificate>();

                        harborCert.Spec.CommonName = clusterLogin.ClusterDefinition.Domain;
                        harborCert.Spec.DnsNames   = new List<string>()
                        {
                            $"{clusterLogin.ClusterDefinition.Domain}",
                            $"*.{clusterLogin.ClusterDefinition.Domain}"
                        };

                        await k8s.ReplaceNamespacedCustomObjectAsync(harborCert, "cert-manager.io", "v1", KubeNamespaces.NeonSystem, "certificates", "registry-harbor");

                        dynamic harborCluster = await k8s.GetNamespacedCustomObjectAsync("goharbor.io", "v1alpha3", KubeNamespaces.NeonSystem, "harborclusters", "registry");

                        harborCluster["spec"]["expose"]["core"]["ingress"]["host"] = $"https://registry.{clusterLogin.ClusterDefinition.Domain}";
                        harborCluster["spec"]["expose"]["notary"]["ingress"]["host"] = $"https://notary.{clusterLogin.ClusterDefinition.Domain}";
                        harborCluster["spec"]["externalURL"] = $"https://registry.{clusterLogin.ClusterDefinition.Domain}";

                        await k8s.ReplaceNamespacedCustomObjectAsync((JObject)harborCluster, "goharbor.io", "v1alpha3", KubeNamespaces.NeonSystem, "harborclusters", "registry");
            });
            }
        }

        /// <summary>
        /// Configures the root Kubernetes user.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateRootUserAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/root-user",
                async () =>
                {
                    controller.LogProgress(master, verb: "create", message: "root user");

                    var userYaml =
$@"
apiVersion: v1
kind: ServiceAccount
metadata:
  name: {KubeConst.RootUser}-user
  namespace: kube-system
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: {KubeConst.RootUser}-user
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: cluster-admin
subjects:
- kind: ServiceAccount
  name: {KubeConst.RootUser}-user
  namespace: kube-system
- kind: Group
  apiGroup: rbac.authorization.k8s.io
  name: superadmin
";
                    master.KubectlApply(userYaml, RunOptions.FaultOnError);

                    await Task.CompletedTask;
                });
        }

        /// <summary>
        /// Generates a dashboard certificate.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The generated certificate.</returns>
        public static TlsCertificate GenerateDashboardCert(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);

            // We're going to tie the custom certificate to the IP addresses
            // of the master nodes only.  This means that only these nodes
            // can accept the traffic and also that we'd need to regenerate
            // the certificate if we add/remove a master node.
            //
            // Here's the tracking task:
            //
            //      https://github.com/nforgeio/neonKUBE/issues/441

            var masterAddresses = new List<string>();

            foreach (var masterNode in cluster.Masters)
            {
                masterAddresses.Add(masterNode.Address.ToString());
            }

            var utcNow     = DateTime.UtcNow;
            var utc10Years = utcNow.AddYears(10);

            var certificate = TlsCertificate.CreateSelfSigned(
                hostnames: masterAddresses,
                validDays: (int)(utc10Years - utcNow).TotalDays,
                issuedBy:  "kubernetes-dashboard");

            return certificate;
        }

        /// <summary>
        /// Configures the root Kubernetes user.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallKubeDashboardAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.KubernetesDashboard);

            await master.InvokeIdempotentAsync("setup/kube-dashboard",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "kubernetes dashboard");

                    var values = new Dictionary<string, object>();

                    values.Add("replicas", serviceAdvice.ReplicaCount);
                    values.Add("cluster.name", cluster.Definition.Name);
                    values.Add("settings.clusterName", cluster.Definition.Name);
                    values.Add("cluster.domain", cluster.Definition.Domain);
                    values.Add("ingress.subdomain", ClusterDomain.KubernetesDashboard);
                    values.Add($"metricsScraper.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    await master.InstallHelmChartAsync(controller, "kubernetes_dashboard", releaseName: "kubernetes-dashboard", @namespace: KubeNamespaces.NeonSystem, values: values, progressMessage: "kubernetes-dashboard");

                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/k8s-ingress",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "update k8s ingress");

                        var virtualService = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "k8s-dashboard-virtual-service")).ToObject<VirtualService>();

                        virtualService.Spec.Hosts =
                            new List<string>()
                            {
                                $"{ClusterDomain.KubernetesDashboard}.{cluster.Definition.Domain}"
                            };

                        await k8s.ReplaceNamespacedCustomObjectAsync(virtualService, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "k8s-dashboard-virtual-service");
                    });
            }
        }

        /// <summary>
        /// Adds the node taints.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task TaintNodesAsync(ISetupController controller)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var master  = cluster.FirstMaster;

            await master.InvokeIdempotentAsync("setup/taint-nodes",
                async () =>
                {
                    controller.LogProgress(master, verb: "taint", message: "nodes");

                    try
                    {
                        // Generate a Bash script we'll submit to the first master
                        // that initializes the taints for all nodes.

                        var sbScript = new StringBuilder();
                        var sbArgs = new StringBuilder();

                        sbScript.AppendLineLinux("#!/bin/bash");

                        foreach (var node in cluster.Nodes)
                        {
                            var taintDefinitions = new List<string>();

                            if (node.Metadata.IsWorker)
                            {
                                // Kubernetes doesn't set the role for worker nodes so we'll do that here.

                                taintDefinitions.Add("kubernetes.io/role=worker");
                            }

                            taintDefinitions.Add($"{NodeLabels.LabelDatacenter}={GetLabelValue(cluster.Definition.Datacenter.ToLowerInvariant())}");
                            taintDefinitions.Add($"{NodeLabels.LabelEnvironment}={GetLabelValue(cluster.Definition.Environment.ToString().ToLowerInvariant())}");

                            if (node.Metadata.Taints != null)
                            {
                                foreach (var taint in node.Metadata.Taints)
                                {
                                    sbScript.AppendLine();
                                    sbScript.AppendLineLinux($"kubectl taint nodes {node.Name} {taint}");
                                }
                            }
                        }

                        master.SudoCommand(CommandBundle.FromScript(sbScript));
                    }
                    finally
                    {
                        master.Status = string.Empty;
                    }

                    await Task.CompletedTask;
                });
        }

        /// <summary>
        /// Deploy Kiali.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private static async Task InstallKialiAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);

            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/kiali",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "kaili");

                    var values = new Dictionary<string, object>();

                    var secret = await k8s.ReadNamespacedSecretAsync(KubeConst.DexSecret, KubeNamespaces.NeonSystem);

                    values.Add("oidc.secret", Encoding.UTF8.GetString(secret.Data["KUBERNETES_CLIENT_SECRET"]));
                    values.Add("image.operator.organization", KubeConst.LocalClusterRegistry);
                    values.Add("image.operator.repository", "kiali-kiali-operator");
                    values.Add("image.kiali.organization", KubeConst.LocalClusterRegistry);
                    values.Add("image.kiali.repository", "kiali-kiali");
                    values.Add("cluster.name", cluster.Definition.Name);
                    values.Add("cluster.domain", cluster.Definition.Domain);
                    values.Add("ingress.subdomain", ClusterDomain.Kiali);
                    values.Add("grafanaPassword", NeonHelper.GetCryptoRandomPassword(20));
                    
                    int i = 0;
                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelIstio, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "kiali", releaseName: "kiali-operator", @namespace: KubeNamespaces.NeonSystem, values: values);
                });

            await master.InvokeIdempotentAsync("setup/kiali-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "kaili");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "kiali-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "kiali", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval)
                        });
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/kiali-ingress",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "update kiali ingress");

                        var virtualService = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "kiali-dashboard-virtual-service")).ToObject<VirtualService>();

                        virtualService.Spec.Hosts =
                            new List<string>()
                            {
                                $"{ClusterDomain.Kiali}.{cluster.Definition.Domain}"
                            };

                        await k8s.ReplaceNamespacedCustomObjectAsync(virtualService, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "kiali-dashboard-virtual-service");
                    });
            }
        }

        /// <summary>
        /// Some initial kubernetes configuration.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task KubeSetupAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/initial-kubernetes", async
                () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "kubernetes");

                    await master.InstallHelmChartAsync(controller, "cluster_setup");
                });
        }

        /// <summary>
        /// Installs the Node Problem Detector.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallNodeProblemDetectorAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.NodeProblemDetector);

            var values = new Dictionary<string, object>();

            values.Add("cluster.name", cluster.Definition.Name);
            values.Add("cluster.domain", cluster.Definition.Domain);
            values.Add($"metrics.serviceMonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

            await master.InvokeIdempotentAsync("setup/node-problem-detector",
                async () =>
                {
                    await master.InstallHelmChartAsync(controller, "node_problem_detector", releaseName: "node-problem-detector", @namespace: KubeNamespaces.NeonSystem);
                });

            await master.InvokeIdempotentAsync("setup/node-problem-detector-ready",
                async () =>
                {
                    await k8s.WaitForDaemonsetAsync(KubeNamespaces.NeonSystem, "node-problem-detector", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs OpenEBS.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallOpenEbsAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster                = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterAdvice          = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var apiServerAdvice        = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.OpenEbsApiServer);
            var provisionerAdvice      = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.OpenEbsProvisioner);
            var localPvAdvice          = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.OpenEbsLocalPvProvisioner);
            var snapshotOperatorAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.OpenEbsSnapshotOperator);
            var ndmOperatorAdvice      = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.OpenEbsNdmOperator);
            var webhookAdvice          = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.OpenEbsWebhook);

            await master.InvokeIdempotentAsync("setup/openebs-all",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "openebs");

                    await master.InvokeIdempotentAsync("setup/openebs",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "setup", message: "openebs-base");

                            var values = new Dictionary<string, object>();

                            values.Add("apiserver.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("helper.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("localprovisioner.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("policies.monitoring.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("snapshotOperator.controller.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("snapshotOperator.provisioner.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("provisioner.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("ndm.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("ndmOperator.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("webhook.image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("jiva.image.organization", KubeConst.LocalClusterRegistry);

                            values.Add($"apiserver.replicas", apiServerAdvice.ReplicaCount);
                            values.Add($"provisioner.replicas", provisionerAdvice.ReplicaCount);
                            values.Add($"localprovisioner.replicas", localPvAdvice.ReplicaCount);
                            values.Add($"snapshotOperator.replicas", snapshotOperatorAdvice.ReplicaCount);
                            values.Add($"ndmOperator.replicas", ndmOperatorAdvice.ReplicaCount);
                            values.Add($"webhook.replicas", webhookAdvice.ReplicaCount);
                            values.Add($"serviceMonitor.interval", clusterAdvice.MetricsInterval);

                            await master.InstallHelmChartAsync(controller, "openebs", releaseName: "openebs", values: values, @namespace: KubeNamespaces.NeonStorage);
                        });

                    switch (cluster.Definition.OpenEbs.Engine)
                    {
                        case OpenEbsEngine.cStor:

                            await DeployOpenEbsWithcStor(controller, master);
                            break;

                        case OpenEbsEngine.HostPath:
                        case OpenEbsEngine.Jiva:
                            
                            await WaitForOpenEbsReady(controller, master);
                            break;

                        default:
                        case OpenEbsEngine.Default:
                        case OpenEbsEngine.Mayastor:

                            throw new NotImplementedException($"[{cluster.Definition.OpenEbs.Engine}]");
                    }
                });
        }

        /// <summary>
        /// Deploys OpenEBS using the cStor engine.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private static async Task DeployOpenEbsWithcStor(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s     = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/openebs-cstor",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "openebs-cstor");

                    var values = new Dictionary<string, object>();

                    values.Add("cspcOperator.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("cspcOperator.poolManager.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("cspcOperator.cstorPool.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("cspcOperator.cstorPoolExporter.image.organization", KubeConst.LocalClusterRegistry);

                    values.Add("cvcOperator.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("cvcOperator.target.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("cvcOperator.volumeMgmt.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("cvcOperator.volumeExporter.image.organization", KubeConst.LocalClusterRegistry);

                    values.Add("csiController.resizer.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("csiController.snapshotter.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("csiController.snapshotController.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("csiController.attacher.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("csiController.provisioner.image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("csiController.driverRegistrar.image.organization", KubeConst.LocalClusterRegistry);

                    values.Add("cstorCSIPlugin.image.organization", KubeConst.LocalClusterRegistry);

                    values.Add("csiNode.driverRegistrar.image.organization", KubeConst.LocalClusterRegistry);

                    values.Add("admissionServer.image.organization", KubeConst.LocalClusterRegistry);

                    await master.InstallHelmChartAsync(controller, "openebs_cstor_operator", releaseName: "openebs-cstor", values: values, @namespace: KubeNamespaces.NeonStorage);
                });

            await WaitForOpenEbsReady(controller, master);

            controller.LogProgress(master, verb: "setup", message: "openebs-pool");

            await master.InvokeIdempotentAsync("setup/openebs-pool",
                async () =>
                {
                    var cStorPoolCluster = new V1CStorPoolCluster()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name              = "cspc-stripe",
                            NamespaceProperty = KubeNamespaces.NeonStorage
                        },
                        Spec = new V1CStorPoolClusterSpec()
                        {
                            Pools = new List<V1CStorPoolSpec>()
                        }
                    };

                    var blockDevices = ((JObject)await k8s.ListNamespacedCustomObjectAsync("openebs.io", "v1alpha1", KubeNamespaces.NeonStorage, "blockdevices")).ToObject<V1CStorBlockDeviceList>();

                    foreach (var node in cluster.Definition.Nodes)
                    {
                        if (blockDevices.Items.Any(device => device.Spec.NodeAttributes.GetValueOrDefault("nodeName") == node.Name))
                        {
                            var pool = new V1CStorPoolSpec()
                            {
                                NodeSelector = new Dictionary<string, string>()
                                {
                                    { "kubernetes.io/hostname", node.Name }
                                },
                                DataRaidGroups = new List<V1CStorDataRaidGroup>()
                                {
                                    new V1CStorDataRaidGroup()
                                    {
                                        BlockDevices = new List<V1CStorBlockDeviceRef>()
                                    }
                                },
                                PoolConfig = new V1CStorPoolConfig()
                                {
                                    DataRaidGroupType = DataRaidGroupType.Stripe,
                                    Tolerations = new List<V1Toleration>()
                                    {
                                        { new V1Toleration() { Effect = "NoSchedule", OperatorProperty = "Exists" } },
                                        { new V1Toleration() { Effect = "NoExecute", OperatorProperty = "Exists" } }
                                    }
                                }
                            };

                            foreach (var device in blockDevices.Items.Where(device => device.Spec.NodeAttributes.GetValueOrDefault("nodeName") == node.Name))
                            {
                                pool.DataRaidGroups.FirstOrDefault().BlockDevices.Add(
                                    new V1CStorBlockDeviceRef()
                                    {
                                        BlockDeviceName = device.Metadata.Name
                                    });
                            }

                            cStorPoolCluster.Spec.Pools.Add(pool);
                        }
                    }

                    k8s.CreateNamespacedCustomObject(cStorPoolCluster, "cstor.openebs.io", "v1", KubeNamespaces.NeonStorage, "cstorpoolclusters");
                });

            await master.InvokeIdempotentAsync("setup/openebs-cstor-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "openebs cstor");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDaemonsetAsync(KubeNamespaces.NeonStorage, "openebs-cstor-csi-node", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-cstor-admission-server", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-cstor-cvc-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-cstor-cspc-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval)
                        });
                });

            var replicas = 3;

            if (cluster.Definition.Nodes.Where(node => node.OpenEbsStorage).Count() < replicas)
            {
                replicas = cluster.Definition.Nodes.Where(node => node.OpenEbsStorage).Count();
            }

            await CreateCstorStorageClass(controller, master, "openebs-cstor", replicaCount: replicas);
        }

        /// <summary>
        /// Waits for OpenEBS to become ready.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private static async Task WaitForOpenEbsReady(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/openebs-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "openebs");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDaemonsetAsync(KubeNamespaces.NeonStorage, "openebs-ndm", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDaemonsetAsync(KubeNamespaces.NeonStorage, "openebs-ndm-node-exporter", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-admission-server", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-apiserver", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-localpv-provisioner", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-ndm-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-ndm-cluster-exporter", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-provisioner", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonStorage, "openebs-snapshot-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval)
                        });
                });
        }

        /// <summary>
        /// Creates a Kubernetes namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">The new Namespace name.</param>
        /// <param name="istioInjectionEnabled">Whether Istio sidecar injection should be enabled.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateNamespaceAsync(
            ISetupController                controller,
            NodeSshProxy<NodeDefinition>    master,
            string                          name,
            bool                            istioInjectionEnabled = true)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync($"setup/namespace-{name}",
                async () =>
                {
                    await k8s.CreateNamespaceAsync(new V1Namespace()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name   = name,
                            Labels = new Dictionary<string, string>()
                            {
                                { "istio-injection", istioInjectionEnabled ? "enabled" : "disabled" }
                            }
                        }
                    });
                });
        }

        /// <summary>
        /// Creates a Kubernetes Storage Class.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">The new <see cref="V1StorageClass"/> name.</param>
        /// <param name="replicaCount">Specifies the data replication factor.</param>
        /// <param name="storagePool">Specifies the OpenEBS storage pool.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateJivaStorageClass(
            ISetupController                controller,
            NodeSshProxy<NodeDefinition>    master,
            string                          name,
            int                             replicaCount = 3,
            string                          storagePool  = "default")
        {
            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync($"setup/storage-class-jiva-{name}",
                async () =>
                {

                    if (master.Cluster.Definition.Nodes.Count() < replicaCount)
                    {
                        replicaCount = master.Cluster.Definition.Nodes.Count();
                    }

                    var storageClass = new V1StorageClass()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = name,
                            Annotations = new Dictionary<string, string>()
                            {
                                {  "cas.openebs.io/config",
$@"- name: ReplicaCount
  value: ""{replicaCount}""
- name: StoragePool
  value: {storagePool}
" },
                                {"openebs.io/cas-type", "jiva" }
                            },
                        },
                        Provisioner       = "openebs.io/provisioner-iscsi",
                        ReclaimPolicy     = "Delete",
                        VolumeBindingMode = "WaitForFirstConsumer"
                    };

                    await k8s.CreateStorageClassAsync(storageClass);
                });
        }

        /// <summary>
        /// Creates a Kubernetes Storage Class.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">The new <see cref="V1StorageClass"/> name.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateHostPathStorageClass(
            ISetupController                controller,
            NodeSshProxy<NodeDefinition>    master,
            string                          name)
        {
            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync($"setup/storage-class-hostpath-{name}",
                async () =>
                {
                    var storageClass = new V1StorageClass()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = name,
                            Annotations = new Dictionary<string, string>()
                            {
                                {  "cas.openebs.io/config",
$@"- name: StorageType
  value: ""hostpath""
- name: BasePath
  value: /var/openebs/local
" },
                                {"openebs.io/cas-type", "local" }
                            },
                        },
                        Provisioner       = "openebs.io/local",
                        ReclaimPolicy     = "Delete",
                        VolumeBindingMode = "WaitForFirstConsumer"
                    };

                    await k8s.CreateStorageClassAsync(storageClass);
                });
        }

        /// <summary>
        /// Creates an OpenEBS cStor Kubernetes Storage Class.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">The new <see cref="V1StorageClass"/> name.</param>
        /// <param name="cstorPoolCluster">Specifies the cStor pool name.</param>
        /// <param name="replicaCount">Specifies the data replication factor.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateCstorStorageClass(
            ISetupController                controller,
            NodeSshProxy<NodeDefinition>    master,
            string                          name,
            string                          cstorPoolCluster = "cspc-stripe",
            int                             replicaCount     = 3)
        {
            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync($"setup/storage-class-cstor-{name}",
                async () =>
                {
                    if (master.Cluster.Definition.Nodes.Where(node => node.OpenEbsStorage).Count() < replicaCount)
                    {
                        replicaCount = master.Cluster.Definition.Nodes.Where(node => node.OpenEbsStorage).Count();
                    }

                    var storageClass = new V1StorageClass()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = name
                        },
                        Parameters = new Dictionary<string, string>
                        {
                            {  "cas-type", "cstor" },
                            {  "cstorPoolCluster", cstorPoolCluster },
                            {  "replicaCount", $"{replicaCount}" },
                        },
                        AllowVolumeExpansion = true,
                        Provisioner          = "cstor.csi.openebs.io",
                        ReclaimPolicy        = "Delete",
                        VolumeBindingMode    = "Immediate"
                    };

                    await k8s.CreateStorageClassAsync(storageClass);
                });
        }

        /// <summary>
        /// Creates the approperiate OpenEBS Kubernetes Storage Class for the cluster.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">The new <see cref="V1StorageClass"/> name.</param>
        /// <param name="replicaCount">Specifies the data replication factor.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateStorageClass(
            ISetupController                controller,
            NodeSshProxy<NodeDefinition>    master,
            string                          name,
            int                             replicaCount = 3)
        {
            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);

            switch (cluster.Definition.OpenEbs.Engine)
            {
                case OpenEbsEngine.Default:

                    throw new InvalidOperationException($"[{nameof(OpenEbsEngine.Default)}] is not valid here.  This must be set to one of the other storage engines in [{nameof(OpenEbsOptions)}.Validate()].");

                case OpenEbsEngine.HostPath:

                    await CreateHostPathStorageClass(controller, master, name);
                    break;

                case OpenEbsEngine.cStor:

                    await CreateCstorStorageClass(controller, master, name);
                    break;

                case OpenEbsEngine.Jiva:

                    await CreateJivaStorageClass(controller, master, name);
                    break;

                case OpenEbsEngine.Mayastor:
                default:

                    throw new NotImplementedException($"Support for the [{cluster.Definition.OpenEbs.Engine}] OpenEBS storage engine is not implemented.");
            };
        }

        /// <summary>
        /// Installs an Etcd cluster to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallEtcdAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s     = GetK8sClient(controller);
            var advice  = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice).GetServiceAdvice(KubeClusterAdvice.EtcdCluster);

            await master.InvokeIdempotentAsync("setup/monitoring-etcd",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "etcd");

                    await CreateStorageClass(controller, master, "neon-internal-etcd");

                    var values = new Dictionary<string, object>();

                    values.Add($"replicas", advice.ReplicaCount);

                    values.Add($"volumeClaimTemplate.resources.requests.storage", "1Gi");

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "etcd_cluster", releaseName: "neon-etcd", @namespace: KubeNamespaces.NeonSystem, values: values);
                });

            await master.InvokeIdempotentAsync("setup/setup/monitoring-etcd-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "etc (monitoring)");

                    await k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonMonitor, "neon-system-etcd", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs The Grafana Agent to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallPrometheusAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster         = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterAdvice   = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var agentAdvice     = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.GrafanaAgent);
            var agentNodeAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.GrafanaAgentNode);
            var istioAdvice     = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.IstioProxy);

            await master.InvokeIdempotentAsync("setup/monitoring-prometheus",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "prometheus");

                    var values = new Dictionary<string, object>();
                    var i      = 0;

                    values.Add($"cluster.name", cluster.Definition.Name);
                    values.Add($"cluster.domain", cluster.Definition.Domain);

                    values.Add($"metrics.global.scrapeInterval", clusterAdvice.MetricsInterval);
                    values.Add($"metrics.crio.scrapeInterval", clusterAdvice.MetricsInterval);
                    values.Add($"metrics.istio.scrapeInterval", istioAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);
                    values.Add($"metrics.kubelet.scrapeInterval", clusterAdvice.MetricsInterval);
                    values.Add($"metrics.cadvisor.scrapeInterval", clusterAdvice.MetricsInterval);

                    if (agentAdvice.PodMemoryRequest != null && agentAdvice.PodMemoryLimit != null)
                    {
                        values.Add($"resources.agent.requests.memory", ToSiString(agentAdvice.PodMemoryRequest.Value));
                        values.Add($"resources.agent.limits.memory", ToSiString(agentAdvice.PodMemoryLimit.Value));
                    }

                    if (agentNodeAdvice.PodMemoryRequest != null && agentNodeAdvice.PodMemoryLimit != null)
                    {
                        values.Add($"resources.agentNode.requests.memory", ToSiString(agentNodeAdvice.PodMemoryRequest.Value));
                        values.Add($"resources.agentNode.limits.memory", ToSiString(agentNodeAdvice.PodMemoryLimit.Value));
                    }

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");

                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "grafana_agent", releaseName: "grafana-agent", @namespace: KubeNamespaces.NeonMonitor, values: values);
                });
        }

        /// <summary>
        /// Waits for Prometheus to be fully ready.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task WaitForPrometheusAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s     = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/monitoring-grafana-agent-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "grafana agent");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonMonitor, "grafana-agent-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDaemonsetAsync(KubeNamespaces.NeonMonitor, "grafana-agent-node", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonMonitor, "grafana-agent", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                        });
                });
        }

        /// <summary>
        /// Installs Cortex to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallCortexAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/monitoring-cortex-all",
                async () =>
                {
                    var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
                    var k8s           = GetK8sClient(controller);
                    var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
                    var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Cortex);

                    var values        = new Dictionary<string, object>();

                    values.Add($"ingress.alertmanager.subdomain", ClusterDomain.AlertManager);
                    values.Add($"ingress.ruler.subdomain", ClusterDomain.CortexRuler);
                    values.Add($"metrics.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    if (cluster.Definition.Nodes.Where(node => node.Labels.Metrics).Count() >= 3)
                    {
                        values.Add($"cortexConfig.distributor.ha_tracker.enable_ha_tracker", true);
                        values.Add($"cortexConfig.distributor.ha_tracker.kvstore.store", "etcd");
                        values.Add($"cortexConfig.distributor.ring.kvstore.store", "etcd");

                        values.Add($"cortexConfig.ingester.lifecycler.ring.kvstore.store", "etcd");
                        values.Add($"cortexConfig.ingester.lifecycler.ring.replication_factor", 3);

                        values.Add($"cortexConfig.ruler.ring.kvstore.store", "etcd");

                        values.Add($"cortexConfig.alertmanager.sharding_enabled", true);
                        values.Add($"cortexConfig.alertmanager.sharding_ring.kvstore.store", "etcd");
                        values.Add($"cortexConfig.alertmanager.sharding_ring.replication_factor", 3);

                        values.Add($"cortexConfig.compactor.sharding_enabled", true);
                        values.Add($"cortexConfig.compactor.sharding_ring.kvstore.store", "etcd");
                        values.Add($"cortexConfig.compactor.sharding_ring.kvstore.replication_factor", 3);
                    }

                    if (serviceAdvice.PodMemoryRequest != null && serviceAdvice.PodMemoryLimit != null)
                    {
                        values.Add($"resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest.Value));
                        values.Add($"resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit.Value));
                    }

                    await master.InvokeIdempotentAsync("setup/monitoring-cortex-secret",
                        async () =>
                        {

                            var dbSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);

                            var citusSecret = new V1Secret()
                            {
                                Metadata = new V1ObjectMeta()
                                {
                                    Name = KubeConst.CitusSecretKey,
                                    NamespaceProperty = KubeNamespaces.NeonMonitor
                                },
                                Data = new Dictionary<string, byte[]>(),
                                StringData = new Dictionary<string, string>()
                            };

                            citusSecret.Data["username"] = dbSecret.Data["username"];
                            citusSecret.Data["password"] = dbSecret.Data["password"];

                            await k8s.UpsertSecretAsync(citusSecret, KubeNamespaces.NeonMonitor);
                        }
                        );

                    await master.InvokeIdempotentAsync("setup/monitoring-cortex",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "setup", message: "cortex");

                            if (cluster.Definition.IsDesktopCluster ||
                                cluster.Definition.Nodes.Any(node => node.Vm.GetMemory(cluster.Definition) < ByteUnits.Parse("4 GiB")))
                            {
                                values.Add($"cortexConfig.ingester.retain_period", $"120s");
                                values.Add($"cortexConfig.ingester.metadata_retain_period", $"5m");
                                values.Add($"cortexConfig.querier.batch_iterators", true);
                                values.Add($"cortexConfig.querier.max_samples", 10000000);
                                values.Add($"cortexConfig.table_manager.retention_period", "12h");
                            }

                            int i = 0;

                            foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                            {
                                values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                                values.Add($"tolerations[{i}].effect", taint.Effect);
                                values.Add($"tolerations[{i}].operator", "Exists");
                                i++;
                            }

                            values.Add("image.organization", KubeConst.LocalClusterRegistry);

                            await master.InstallHelmChartAsync(controller, "cortex", releaseName: "cortex", @namespace: KubeNamespaces.NeonMonitor, values: values);
                        });

                    await master.InvokeIdempotentAsync("setup/monitoring-cortex-ready",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "wait for", message: "cortex");

                            await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonMonitor, "cortex", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                        });
                });
        }

        /// <summary>
        /// Installs Loki to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallLokiAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster        = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s            = GetK8sClient(controller);
            var clusterAdvice  = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice  = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Loki);

            await master.InvokeIdempotentAsync("setup/monitoring-loki",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "loki");

                    var values = new Dictionary<string, object>();

                    values.Add("image.organization", KubeConst.LocalClusterRegistry);
                    
                    values.Add($"replicas", serviceAdvice.ReplicaCount);
                    values.Add($"serviceMonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);
                    
                    if (cluster.Definition.Nodes.Where(node => node.Labels.Metrics).Count() >= 3)
                    {
                        values.Add($"config.common.ring.kvstore.store", "etcd");
                        values.Add($"config.common.ring.kvstore.replication_factor", 3);

                    }

                    if (cluster.Definition.IsDesktopCluster)
                    {
                        values.Add($"config.limits_config.reject_old_samples_max_age", "15m");
                    }

                    if (serviceAdvice.PodMemoryRequest != null && serviceAdvice.PodMemoryLimit != null)
                    {
                        values.Add($"resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest.Value));
                        values.Add($"resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit.Value));
                    }

                    await master.InstallHelmChartAsync(controller, "loki", releaseName: "loki", @namespace: KubeNamespaces.NeonMonitor, values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-loki-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "loki");

                    await k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonMonitor, "loki", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs Tempo to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallTempoAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster        = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s            = GetK8sClient(controller);
            var clusterAdvice  = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var advice         = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Tempo);

            await master.InvokeIdempotentAsync("setup/monitoring-tempo",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "tempo");

                    var values = new Dictionary<string, object>();

                    values.Add("tempo.organization", KubeConst.LocalClusterRegistry);

                    values.Add($"replicas", advice.ReplicaCount);
                    values.Add($"serviceMonitor.interval", advice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    if (cluster.Definition.Nodes.Where(node => node.Labels.Metrics).Count() >= 3)
                    {
                        values.Add($"config.ingester.lifecycler.ring.kvstore.store", "etcd");
                        values.Add($"config.ingester.lifecycler.ring.kvstore.replication_factor", 3);
                    }

                    if (advice.PodMemoryRequest != null && advice.PodMemoryLimit != null)
                    {
                        values.Add($"resources.requests.memory", ToSiString(advice.PodMemoryRequest.Value));
                        values.Add($"resources.limits.memory", ToSiString(advice.PodMemoryLimit.Value));
                    }

                    await master.InstallHelmChartAsync(controller, "tempo", releaseName: "tempo", @namespace: KubeNamespaces.NeonMonitor, values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-tempo-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "tempo");

                    await k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonMonitor, "tempo", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs Kube State Metrics to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallKubeStateMetricsAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.KubeStateMetrics);

            await master.InvokeIdempotentAsync("setup/monitoring-kube-state-metrics",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "kube-state-metrics");

                    var values = new Dictionary<string, object>();

                    values.Add($"prometheus.monitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    await master.InstallHelmChartAsync(controller, "kube_state_metrics", releaseName: "kube-state-metrics", @namespace: KubeNamespaces.NeonMonitor, values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-kube-state-metrics-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "kube-state-metrics");

                    await k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonMonitor, "kube-state-metrics", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs Reloader to the Neon system nnamespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallReloaderAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster        = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s            = GetK8sClient(controller);
            var clusterAdvice  = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice  = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Reloader);

            await master.InvokeIdempotentAsync("setup/reloader",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "reloader");

                    var values = new Dictionary<string, object>();

                    values.Add($"reloader.serviceMonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    await master.InstallHelmChartAsync(controller, "reloader", releaseName: "reloader", @namespace: KubeNamespaces.NeonSystem, values: values);
                });

            await master.InvokeIdempotentAsync("setup/reloader-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "reloader");

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "reloader", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs Grafana to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallGrafanaAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Grafana);

            await master.InvokeIdempotentAsync("setup/monitoring-grafana",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "setup", message: "grafana");

                        var values = new Dictionary<string, object>();

                        values.Add("cluster.name", cluster.Definition.Name);
                        values.Add("cluster.domain", cluster.Definition.Domain);
                        values.Add("ingress.subdomain", ClusterDomain.Grafana);
                        values.Add($"metrics.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                        await master.InvokeIdempotentAsync("setup/db-credentials-grafana",
                            async () =>
                            {
                                var secret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);
                                var dexSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.DexSecret, KubeNamespaces.NeonSystem);

                                var monitorSecret = new V1Secret()
                                {
                                    Metadata = new V1ObjectMeta()
                                    {
                                        Name = KubeConst.GrafanaSecret,
                                        Annotations = new Dictionary<string, string>()
                                        {
                                            {  "reloader.stakater.com/match", "true" }
                                        }
                                    },
                                    Type = "Opaque",
                                    Data = new Dictionary<string, byte[]>()
                                    {
                                        { "DATABASE_PASSWORD", secret.Data["password"] },
                                        { "CLIENT_ID", Encoding.UTF8.GetBytes("grafana") },
                                        { "CLIENT_SECRET", dexSecret.Data["GRAFANA_CLIENT_SECRET"] },
                                    }
                                };

                                await k8s.CreateNamespacedSecretAsync(monitorSecret, KubeNamespaces.NeonMonitor);
                            });

                        int i = 0;

                        foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                        {
                            values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                            values.Add($"tolerations[{i}].effect", taint.Effect);
                            values.Add($"tolerations[{i}].operator", "Exists");
                            i++;
                        }

                        if (serviceAdvice.PodMemoryRequest.HasValue && serviceAdvice.PodMemoryLimit.HasValue)
                        {
                            values.Add($"resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest));
                            values.Add($"resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit));
                        }

                        await master.InstallHelmChartAsync(controller, "grafana", releaseName: "grafana", @namespace: KubeNamespaces.NeonMonitor, values: values);
                    });

            await master.InvokeIdempotentAsync("setup/monitoring-grafana-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "grafana");

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            try
                            {
                                var configmap = await k8s.ReadNamespacedConfigMapAsync("grafana-datasources", KubeNamespaces.NeonMonitor);

                                if (configmap.Data == null || configmap.Data.Keys.Count < 3)
                                {
                                    await (await k8s.ReadNamespacedDeploymentAsync("grafana-operator", KubeNamespaces.NeonMonitor)).RestartAsync(k8s);
                                    return false;
                                }
                            } 
                            catch
                            {
                                return false;
                            }

                            return true;
                        }, TimeSpan.FromMinutes(5),
                        TimeSpan.FromSeconds(60));

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonMonitor, "grafana-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonMonitor, "grafana-deployment", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-grafana-kiali-user",
                async () =>
                {
                    controller.LogProgress(master, verb: "create", message: "kiali-grafana-user");

                    var grafanaSecret = await k8s.ReadNamespacedSecretAsync("grafana-admin-credentials", KubeNamespaces.NeonMonitor);
                    var grafanaUser = Encoding.UTF8.GetString(grafanaSecret.Data["GF_SECURITY_ADMIN_USER"]);
                    var grafanaPassword = Encoding.UTF8.GetString(grafanaSecret.Data["GF_SECURITY_ADMIN_PASSWORD"]);
                    var kialiSecret = await k8s.ReadNamespacedSecretAsync("kiali", KubeNamespaces.NeonSystem);
                    var kialiPassword = Encoding.UTF8.GetString(kialiSecret.Data["grafanaPassword"]);

                    var cmd = new string[]
                    {
                        "/bin/bash",
                        "-c",
                        $@"wget -q -O- --post-data='{{""name"":""kiali"",""email"":""kiali@cluster.local"",""login"":""kiali"",""password"":""{kialiPassword}"",""OrgId"":1}}' --header='Content-Type:application/json' http://{grafanaUser}:{grafanaPassword}@localhost:3000/api/admin/users"
                    };
                    var pod = (await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonMonitor, labelSelector: "app=grafana")).Items.First();
                    (await k8s.NamespacedPodExecAsync(pod.Namespace(), pod.Name(), "grafana", cmd)).EnsureSuccess();
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/grafana-secrets",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "renew grafana secrets");

                        var dbSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);
                        var grafanaSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.GrafanaSecret, KubeNamespaces.NeonMonitor);

                        grafanaSecret.Data["DATABASE_PASSWORD"] = dbSecret.Data["password"];

                        var grafanaAdminSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.GrafanaAdminSecret, KubeNamespaces.NeonMonitor);

                        grafanaSecret.Data["GF_SECURITY_ADMIN_PASSWORD"] = Encoding.UTF8.GetBytes(NeonHelper.GetCryptoRandomPassword(20));
                        grafanaSecret.Data["GF_SECURITY_ADMIN_USER"] = Encoding.UTF8.GetBytes(NeonHelper.GetCryptoRandomPassword(20));

                    });

                await master.InvokeIdempotentAsync("ready-to-go/grafana-ingress",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "update grafana ingress");
                    
                        var virtualService = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "grafana-dashboard-virtual-service")).ToObject<VirtualService>();

                        virtualService.Spec.Hosts =
                            new List<string>()
                            {
                                $"{ClusterDomain.Grafana}.{cluster.Definition.Domain}"
                            };

                        await k8s.ReplaceNamespacedCustomObjectAsync(virtualService, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "grafana-dashboard-virtual-service");
                    });
            }
        }

        /// <summary>
        /// Installs a Minio cluster to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallMinioAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Minio);

            await master.InvokeIdempotentAsync("setup/minio-all",
                async () =>
                {
                    await CreateHostPathStorageClass(controller, master, "neon-internal-minio");

                    await master.InvokeIdempotentAsync("setup/minio",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "setup", message: "minio");

                            var values = new Dictionary<string, object>();

                            values.Add("cluster.name", cluster.Definition.Name);
                            values.Add("cluster.domain", cluster.Definition.Domain);
                            values.Add($"metrics.serviceMonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);
                            values.Add("image.organization", KubeConst.LocalClusterRegistry);
                            values.Add("mcImage.organization", KubeConst.LocalClusterRegistry);
                            values.Add("helmKubectlJqImage.organization", KubeConst.LocalClusterRegistry);
                            values.Add($"tenants[0].pools[0].servers", serviceAdvice.ReplicaCount);
                            values.Add("ingress.operator.subdomain", ClusterDomain.Minio);

                            if (serviceAdvice.ReplicaCount > 1)
                            {
                                values.Add($"mode", "distributed");
                            }

                            if (serviceAdvice.PodMemoryRequest.HasValue && serviceAdvice.PodMemoryLimit.HasValue)
                            {
                                values.Add($"tenants[0].pools[0].resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest));
                                values.Add($"tenants[0].pools[0].resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit));
                            }
                            
                            var accessKey = NeonHelper.GetCryptoRandomPassword(20);
                            var secretKey = NeonHelper.GetCryptoRandomPassword(20);

                            values.Add($"tenants[0].secrets.accessKey", accessKey);
                            values.Add($"clients.aliases.minio.accessKey", accessKey);
                            values.Add($"tenants[0].secrets.secretKey", secretKey);
                            values.Add($"clients.aliases.minio.secretKey", secretKey);

                            values.Add($"tenants[0].console.secrets.passphrase", NeonHelper.GetCryptoRandomPassword(10));
                            values.Add($"tenants[0].console.secrets.salt", NeonHelper.GetCryptoRandomPassword(10));
                            values.Add($"tenants[0].console.secrets.accessKey", NeonHelper.GetCryptoRandomPassword(20));
                            values.Add($"tenants[0].console.secrets.secretKey", NeonHelper.GetCryptoRandomPassword(20));

                            int i = 0;

                            foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelMetricsInternal, "true"))
                            {
                                values.Add($"tenants[0].pools[0].tolerations[{i}].key", serviceAdvice.ReplicaCount);
                                values.Add($"tenants[0].pools[0].tolerations[{i}].effect", serviceAdvice.ReplicaCount);
                                values.Add($"tenants[0].pools[0].tolerations[{i}].operator", serviceAdvice.ReplicaCount);

                                values.Add($"console.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                                values.Add($"console.tolerations[{i}].effect", taint.Effect);
                                values.Add($"console.tolerations[{i}].operator", "Exists");

                                values.Add($"operator.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                                values.Add($"operator.tolerations[{i}].effect", taint.Effect);
                                values.Add($"operator.tolerations[{i}].operator", "Exists");
                                i++;
                            }

                            await master.InstallHelmChartAsync(controller, "minio", releaseName: "minio", @namespace: KubeNamespaces.NeonSystem, values: values);
                        });

                    await master.InvokeIdempotentAsync("configure/minio-secrets",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "configure", message: "minio secret");

                            var secret = await k8s.ReadNamespacedSecretAsync("minio", KubeNamespaces.NeonSystem);

                            secret.Metadata.NamespaceProperty = "monitoring";

                            var monitoringSecret = new V1Secret()
                            {
                                Metadata = new V1ObjectMeta()
                                {
                                    Name = secret.Name(),
                                    Annotations = new Dictionary<string, string>()
                                    {
                                        { "reloader.stakater.com/match", "true" }
                                    }
                                },
                                Data = secret.Data,
                            };
                            await k8s.CreateNamespacedSecretAsync(monitoringSecret, KubeNamespaces.NeonMonitor);
                        });

                    await master.InvokeIdempotentAsync("setup/minio-ready",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "wait for", message: "minio");

                            await NeonHelper.WaitAllAsync(
                                new List<Task>()
                                {
                                    k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonSystem, labelSelector: "app=minio", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                                    k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "minio-console", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                                    k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "minio-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                                });
                        });

                    await master.InvokeIdempotentAsync("setup/minio-policy",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "wait for", message: "minio");

                            var minioPod = (await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem, labelSelector: "app.kubernetes.io/name=minio-operator")).Items.First();

                            await k8s.NamespacedPodExecAsync(
                                KubeNamespaces.NeonSystem,
                                minioPod.Name(),
                                "minio-operator",
                                new string[] {
                                    "/bin/bash",
                                    "-c",
                                    $@"echo '{{""Version"":""2012-10-17"",""Statement"":[{{""Effect"":""Allow"",""Action"":[""admin:*""]}},{{""Effect"":""Allow"",""Action"":[""s3:*""],""Resource"":[""arn:aws:s3:::*""]}}]}}' > /tmp/superadmin.json"
                                });

                            await k8s.NamespacedPodExecAsync(
                                KubeNamespaces.NeonSystem,
                                minioPod.Name(),
                                "minio-operator",
                                new string[] {
                                    "/bin/bash",
                                    "-c",
                                    $"/mc admin policy add minio superadmin /tmp/superadmin.json"
                                });
                        });
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/minio-secrets",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "renew minio secret");

                        var secret = await k8s.ReadNamespacedSecretAsync("minio", KubeNamespaces.NeonSystem);

                        secret.Data["accesskey"] = Encoding.UTF8.GetBytes(NeonHelper.GetCryptoRandomPassword(20));
                        secret.Data["secretkey"] = Encoding.UTF8.GetBytes(NeonHelper.GetCryptoRandomPassword(20));
                        await k8s.ReplaceNamespacedSecretAsync(secret, "minio", KubeNamespaces.NeonSystem);

                        var monitoringSecret = await k8s.ReadNamespacedSecretAsync("minio", KubeNamespaces.NeonMonitor);

                        monitoringSecret.Data["accesskey"] = secret.Data["accesskey"];
                        monitoringSecret.Data["secretkey"] = secret.Data["secretkey"];
                        await k8s.ReplaceNamespacedSecretAsync(monitoringSecret, monitoringSecret.Name(), KubeNamespaces.NeonMonitor);

                        var registrySecret = await k8s.ReadNamespacedSecretAsync("registry-minio", KubeNamespaces.NeonSystem);

                        registrySecret.Data["accesskey"] = secret.Data["accesskey"];
                        registrySecret.Data["secretkey"] = secret.Data["secretkey"];
                        await k8s.ReplaceNamespacedSecretAsync(registrySecret, registrySecret.Name(), KubeNamespaces.NeonSystem);

                        // Delete certs so that they will be regenerated.

                        await k8s.DeleteNamespacedSecretAsync("operator-tls", KubeNamespaces.NeonSystem);
                        await k8s.DeleteNamespacedSecretAsync("operator-webhook-secret", KubeNamespaces.NeonSystem);

                        // Restart minio components.

                        var minioOperator = await k8s.ReadNamespacedDeploymentAsync("minio-operator", KubeNamespaces.NeonSystem);

                        await minioOperator.RestartAsync(GetK8sClient(controller));

                        var minioStatefulSet = (await k8s.ListNamespacedStatefulSetAsync(KubeNamespaces.NeonSystem, labelSelector: "app=minio")).Items.FirstOrDefault();
                        await minioStatefulSet.RestartAsync(GetK8sClient(controller));
                    
                        // Restart registry components.

                        var harborChartmuseum = await k8s.ReadNamespacedDeploymentAsync("registry-harbor-harbor-chartmuseum", KubeNamespaces.NeonSystem);

                        await harborChartmuseum.RestartAsync(GetK8sClient(controller));

                        var harborCore = await k8s.ReadNamespacedDeploymentAsync("registry-harbor-harbor-core", KubeNamespaces.NeonSystem);

                        await harborCore.RestartAsync(GetK8sClient(controller));

                        var harborRegistry = await k8s.ReadNamespacedDeploymentAsync("registry-harbor-harbor-registry", KubeNamespaces.NeonSystem);

                        await harborRegistry.RestartAsync(GetK8sClient(controller));

                        var harborRegistryctl = await k8s.ReadNamespacedDeploymentAsync("registry-harbor-harbor-registryctl", KubeNamespaces.NeonSystem);

                        await harborRegistryctl.RestartAsync(GetK8sClient(controller));
                    });

                await master.InvokeIdempotentAsync("ready-to-go/minio-ingress",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "update minio ingress");

                        var virtualService = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "minio-operator-dashboard-virtual-service")).ToObject<VirtualService>();

                        virtualService.Spec.Hosts =
                            new List<string>()
                            {
                                $"{ClusterDomain.Minio}.{cluster.Definition.Domain}"
                            };

                        await k8s.ReplaceNamespacedCustomObjectAsync(virtualService, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "minio-operator-dashboard-virtual-service");
                    });
            }
        }

        /// <summary>
        /// Installs an Neon Monitoring to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallMonitoringAsync(ISetupController controller)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var master  = cluster.FirstMaster;
            var tasks   = new List<Task>();

            controller.LogProgress(master, verb: "setup", message: "cluster metrics");

            tasks.Add(WaitForPrometheusAsync(controller, master));
            tasks.Add(InstallCortexAsync(controller, master));
            tasks.Add(InstallLokiAsync(controller, master));
            tasks.Add(InstallKubeStateMetricsAsync(controller, master));
            tasks.Add(InstallTempoAsync(controller, master));
            tasks.Add(InstallGrafanaAsync(controller, master));

            await NeonHelper.WaitAllAsync(tasks);
        }

        /// <summary>
        /// Installs Jaeger
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <remarks>The tracking <see cref="Task"/>.</remarks>
        public static async Task InstallJaegerAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/monitoring-jaeger",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "jaeger");

                    var values = new Dictionary<string, object>();

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelLogs, "true"))
                    {
                        values.Add($"ingester.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"ingester.tolerations[{i}].effect", taint.Effect);
                        values.Add($"ingester.tolerations[{i}].operator", "Exists");

                        values.Add($"agent.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"agent.tolerations[{i}].effect", taint.Effect);
                        values.Add($"agent.tolerations[{i}].operator", "Exists");

                        values.Add($"collector.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"collector.tolerations[{i}].effect", taint.Effect);
                        values.Add($"collector.tolerations[{i}].operator", "Exists");

                        values.Add($"query.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"query.tolerations[{i}].effect", taint.Effect);
                        values.Add($"query.tolerations[{i}].operator", "Exists");

                        values.Add($"esIndexCleaner.tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"esIndexCleaner.tolerations[{i}].effect", taint.Effect);
                        values.Add($"esIndexCleaner.tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "jaeger", releaseName: "jaeger", @namespace: KubeNamespaces.NeonMonitor, values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-jaeger-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "jaeger");

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await k8s.ListNamespacedDeploymentAsync(KubeNamespaces.NeonMonitor, labelSelector: "release=jaeger");

                            if (deployments == null || deployments.Items.Count < 2)
                            {
                                return false;
                            }

                            return deployments.Items.All(deployment => deployment.Status.AvailableReplicas == deployment.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpPollInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs a harbor container registry and required components.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallRedisAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Redis);

            await master.InvokeIdempotentAsync("setup/redis",
                async () =>
                {
                    await SyncContext.ClearAsync;

                    controller.LogProgress(master, verb: "setup", message: "redis");

                    var values   = new Dictionary<string, object>();

                    values.Add("image.organization", KubeConst.LocalClusterRegistry);
                    values.Add($"replicas", serviceAdvice.ReplicaCount);
                    values.Add($"exporter.serviceMonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    if (serviceAdvice.ReplicaCount < 2)
                    {
                        values.Add($"hardAntiAffinity", false);
                        values.Add($"sentinel.quorum", 1);
                    }

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystemRegistry, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "redis_ha", releaseName: "neon-redis", @namespace: KubeNamespaces.NeonSystem, values: values);
                });

            await master.InvokeIdempotentAsync("setup/redis-ready",
                async () =>
                {
                    await SyncContext.ClearAsync;

                    controller.LogProgress(master, verb: "wait for", message: "redis");

                    await k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonSystem, "neon-redis-server", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs a harbor container registry and required components.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallHarborAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var clusterLogin  = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Harbor);

            await master.InvokeIdempotentAsync("configure/registry-minio-secret",
                async () =>
                {
                    controller.LogProgress(master, verb: "configure", message: "minio secret");

                    var minioSecret = await k8s.ReadNamespacedSecretAsync("minio", KubeNamespaces.NeonSystem);

                    var secret = new V1Secret()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name              = "registry-minio",
                            NamespaceProperty = KubeNamespaces.NeonSystem,
                            Annotations       = new Dictionary<string, string>()
                            {
                                {  "reloader.stakater.com/match", "true" }
                            }
                        },
                        Type = "Opaque",
                        Data = new Dictionary<string, byte[]>()
                        {
                            { "secret", minioSecret.Data["secretkey"] }
                        }
                    };

                    await k8s.CreateNamespacedSecretAsync(secret, KubeNamespaces.NeonSystem);
                });

            await master.InvokeIdempotentAsync("setup/harbor-db",
                async () =>
                {
                    controller.LogProgress(master, verb: "configure", message: "harbor databases");

                    await CreateStorageClass(controller, master, "neon-internal-registry");

                    // Create the Harbor databases.

                    var dbSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);

                    var harborSecret = new V1Secret()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name              = KubeConst.RegistrySecretKey,
                            NamespaceProperty = KubeNamespaces.NeonSystem
                        },
                        Data       = new Dictionary<string, byte[]>(),
                        StringData = new Dictionary<string, string>()
                    };

                    if ((await k8s.ListNamespacedSecretAsync(KubeNamespaces.NeonSystem)).Items.Any(s => s.Metadata.Name == KubeConst.RegistrySecretKey))
                    {
                        harborSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.RegistrySecretKey, KubeNamespaces.NeonSystem);

                        if (harborSecret.Data == null)
                        {
                            harborSecret.Data = new Dictionary<string, byte[]>();
                        }

                        harborSecret.StringData = new Dictionary<string, string>();
                    }

                    if (!harborSecret.Data.ContainsKey("postgresql-password"))
                    {
                        harborSecret.Data["postgresql-password"] = dbSecret.Data["password"];

                        await k8s.UpsertSecretAsync(harborSecret, KubeNamespaces.NeonSystem);
                    }

                    if (!harborSecret.Data.ContainsKey("secret"))
                    {
                        harborSecret.StringData["secret"] = NeonHelper.GetCryptoRandomPassword(20);

                        await k8s.UpsertSecretAsync(harborSecret, KubeNamespaces.NeonSystem);
                    }

                    var databases = new string[] { "core", "clair", "notaryserver", "notarysigner" };

                    foreach (var db in databases)
                    {
                        await CreateSystemDatabaseAsync(controller, master, $"{KubeConst.NeonSystemDbHarborPrefix}_{db}", KubeConst.NeonSystemDbServiceUser, Encoding.UTF8.GetString(dbSecret.Data["password"]));
                    }
                });

                await master.InvokeIdempotentAsync("setup/harbor",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "configure", message: "harbor minio");

                        // Create the Harbor Minio bucket.

                        var minioSecret = await k8s.ReadNamespacedSecretAsync("minio", KubeNamespaces.NeonSystem);
                        var accessKey   = Encoding.UTF8.GetString(minioSecret.Data["accesskey"]);
                        var secretKey   = Encoding.UTF8.GetString(minioSecret.Data["secretkey"]);
                        var ldapSecret  = await k8s.ReadNamespacedSecretAsync(KubeConst.DexSecret, KubeNamespaces.NeonSystem);

                        await CreateMinioBucketAsync(controller, master, "harbor");

                        // Install the Harbor Helm chart.

                        var values = new Dictionary<string, object>();


                        values.Add("cluster.name", cluster.Definition.Name);
                        values.Add("cluster.domain", cluster.Definition.Domain);
                        values.Add($"metrics.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                        values.Add("ingress.notary.subdomain", ClusterDomain.HarborNotary);
                        values.Add("ingress.registry.subdomain", ClusterDomain.HarborRegistry);
                        
                        values.Add($"storage.s3.accessKey", Encoding.UTF8.GetString(minioSecret.Data["accesskey"]));
                        values.Add($"storage.s3.secretKeyRef", "registry-minio");

                        var baseDN = $@"dc={string.Join($@"\,dc=", cluster.Definition.Domain.Split('.'))}";
                        values.Add($"ldap.baseDN", baseDN);
                        values.Add($"ldap.secret", Encoding.UTF8.GetString(ldapSecret.Data["LDAP_SECRET"]));
                        

                        int j = 0;

                        foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystemRegistry, "true"))
                        {
                            values.Add($"tolerations[{j}].key", $"{taint.Key.Split("=")[0]}");
                            values.Add($"tolerations[{j}].effect", taint.Effect);
                            values.Add($"tolerations[{j}].operator", "Exists");
                            j++;
                        }

                        await master.InstallHelmChartAsync(controller, "harbor", releaseName: "registry-harbor", @namespace: KubeNamespaces.NeonSystem, values: values);
                    });

            await master.InvokeIdempotentAsync("setup/harbor-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "harbor");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-chartmuseum", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-core", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-jobservice", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-notaryserver", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-notarysigner", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-portal", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-registry", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-registryctl", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "registry-harbor-harbor-trivy", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval)
                        });
                });

            await master.InvokeIdempotentAsync($"{(readyToGoMode == ReadyToGoMode.Setup ? "ready-to-go" : "setup")}/harbor-login",
                async () =>
                {
                    controller.LogProgress(master, verb: "images", message: "push");
                    
                    if (readyToGoMode == ReadyToGoMode.Setup)
                    {
                        var secret = await k8s.ReadNamespacedSecretAsync("registry", KubeNamespaces.NeonSystem);

                        secret.Data["secret"] = Encoding.UTF8.GetBytes(NeonHelper.GetCryptoRandomPassword(20)); ;
                        await k8s.UpsertSecretAsync(secret, secret.Namespace());

                        // Delete secret so that the harbor operator creates a new one with the updated credential.

                        await k8s.DeleteNamespacedSecretAsync("registry-harbor-harbor-registry-basicauth", KubeNamespaces.NeonSystem);

                        var command = new string[]
                        {
                            "psql",
                            "--username", "neon_admin",
                            "-d", "harbor_core",
                            "-c", $@"UPDATE public.harbor_user SET ""password""='', salt = '' WHERE user_id = 1;"
                        };

                        var result = await k8s.NamespacedPodExecAsync(
                            name:       "db-citus-postgresql-master-0",
                            @namespace: KubeNamespaces.NeonSystem, 
                            container:  "citus", 
                            command:    command);

                        result.EnsureSuccess();

                        // Restart harbor core so that it picks up the new secret.

                        var harborCore = await k8s.ReadNamespacedDeploymentAsync("registry-harbor-harbor-core", KubeNamespaces.NeonSystem);

                        await harborCore.RestartAsync(GetK8sClient(controller));
                    }

                    var authSecret = await k8s.ReadNamespacedSecretAsync("glauth-users", KubeNamespaces.NeonSystem);

                    var username = "root";
                    var password = Encoding.UTF8.GetString(authSecret.Data[username]);
                    var sbScript = new StringBuilder();
                    var sbArgs   = new StringBuilder();

                    sbScript.AppendLineLinux("#!/bin/bash");
                    sbScript.AppendLineLinux($"echo '{password}' | podman login neon-registry.node.local --username {username} --password-stdin");

                    foreach (var node in cluster.Nodes)
                    {
                        master.SudoCommand(CommandBundle.FromScript(sbScript), RunOptions.FaultOnError);
                    }
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                var virtualService = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "harbor-virtual-service")).ToObject<VirtualService>();

                virtualService.Spec.Hosts =
                    new List<string>()
                    {
                        $"{ClusterDomain.HarborRegistry}.{cluster.Definition.Domain}",
                        $"{ClusterDomain.HarborNotary}.{cluster.Definition.Domain}",
                        KubeConst.LocalClusterRegistry
                    };

                await k8s.ReplaceNamespacedCustomObjectAsync(virtualService, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "harbor-virtual-service");               
            }
        }

        /// <summary>
        /// Installs the Neon Cluster Operator.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallClusterOperatorAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            // $todo(jefflill): Temporarily disabling setup until the refactor is complete
            //
            //      https://github.com/nforgeio/neonKUBE/issues/1302#issuecomment-999883172
#if TODO
            var k8s = GetK8sClient(controller);

            await master.InvokeIdempotentAsync("setup/cluster-operator",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "neon-cluster-operator");

                    var values = new Dictionary<string, object>();

                    values.Add("image.organization", KubeConst.LocalClusterRegistry);
                    values.Add("image.tag", KubeVersions.NeonKubeContainerImageTag);

                    int i = 0;
                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystem, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "neon_cluster_operator", releaseName: "neon-cluster-operator", @namespace: KubeNamespaces.NeonSystem, values: values);
                });

            await master.InvokeIdempotentAsync("setup/cluster-operator-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "neon-cluster-operator");

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "neon-cluster-operator", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
#endif // TODO
        }

        /// <summary>
        /// Creates required namespaces.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task<List<Task>> CreateNamespacesAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await SyncContext.ClearAsync;

            var tasks = new List<Task>();

            tasks.Add(CreateNamespaceAsync(controller, master, KubeNamespaces.NeonMonitor, true));
            tasks.Add(CreateNamespaceAsync(controller, master, KubeNamespaces.NeonStorage, false));
            tasks.Add(CreateNamespaceAsync(controller, master, KubeNamespaces.NeonSystem, true));

            return await Task.FromResult(tasks);
        }

        /// <summary>
        /// Installs a Citus-postgres database used by neon-system services.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallSystemDbAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var managerAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.CitusPostgresSqlManager);
            var masterAdvice  = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.CitusPostgresSqlMaster);
            var workerAdvice  = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.CitusPostgresSqlWorker);

            var values = new Dictionary<string, object>();

            values.Add($"image.organization", KubeConst.LocalClusterRegistry);
            values.Add($"busybox.image.organization", KubeConst.LocalClusterRegistry);
            values.Add($"prometheus.image.organization", KubeConst.LocalClusterRegistry);
            values.Add($"manager.image.organization", KubeConst.LocalClusterRegistry);
            values.Add($"manager.namespace", KubeNamespaces.NeonSystem);
            values.Add($"metrics.interval", managerAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

            if (cluster.Definition.IsDesktopCluster)
            {
                values.Add($"worker.persistence.size", "1Gi");
                values.Add($"master.persistence.size", "1Gi");
            }

            await CreateStorageClass(controller, master, "neon-internal-citus-master");
            await CreateStorageClass(controller, master, "neon-internal-citus-worker");

            if (managerAdvice.PodMemoryRequest.HasValue && managerAdvice.PodMemoryLimit.HasValue)
            {
                values.Add($"manager.resources.requests.memory", ToSiString(managerAdvice.PodMemoryRequest));
                values.Add($"manager.resources.limits.memory", ToSiString(managerAdvice.PodMemoryLimit));
            }

            if (masterAdvice.PodMemoryRequest.HasValue && masterAdvice.PodMemoryLimit.HasValue)
            {
                values.Add($"master.resources.requests.memory", ToSiString(masterAdvice.PodMemoryRequest));
                values.Add($"master.resources.limits.memory", ToSiString(masterAdvice.PodMemoryLimit));
            }

            if (workerAdvice.PodMemoryRequest.HasValue && workerAdvice.PodMemoryLimit.HasValue)
            {
                values.Add($"worker.resources.requests.memory", ToSiString(workerAdvice.PodMemoryRequest));
                values.Add($"worker.resources.limits.memory", ToSiString(workerAdvice.PodMemoryLimit));
            }

            await master.InvokeIdempotentAsync("setup/db-credentials-admin",
                async () =>
                {
                    var username = KubeConst.NeonSystemDbAdminUser;
                    var password = NeonHelper.GetCryptoRandomPassword(20);

                    values.Add($"superuser.password", password);
                    values.Add($"superuser.username", KubeConst.NeonSystemDbAdminUser);

                    var secret = new V1Secret()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = KubeConst.NeonSystemDbAdminSecret,
                            Annotations = new Dictionary<string, string>()
                            {
                                {  "reloader.stakater.com/match", "true" }
                            }
                        },
                        Type = "Opaque",
                        StringData = new Dictionary<string, string>()
                        {
                            { "username", username },
                            { "password", password }
                        }
                    };

                    await k8s.CreateNamespacedSecretAsync(secret, KubeNamespaces.NeonSystem);
                });

            await master.InvokeIdempotentAsync("setup/db-credentials-service",
                async () =>
                {
                    var username = KubeConst.NeonSystemDbServiceUser;
                    var password = NeonHelper.GetCryptoRandomPassword(20);

                    var secret = new V1Secret()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = KubeConst.NeonSystemDbServiceSecret,
                            Annotations = new Dictionary<string, string>()
                            {
                                {  "reloader.stakater.com/match", "true" } 
                            }
                        },
                        Type = "Opaque",
                        StringData = new Dictionary<string, string>()
                        {
                            { "username", username },
                            { "password", password }
                        }
                    };

                    await k8s.CreateNamespacedSecretAsync(secret, KubeNamespaces.NeonSystem);
                });

            await master.InvokeIdempotentAsync("setup/system-db",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "cluster database (citus)");

                    values.Add($"manager.replicas", managerAdvice.ReplicaCount);
                    values.Add($"master.replicas", masterAdvice.ReplicaCount);
                    values.Add($"worker.replicas", workerAdvice.ReplicaCount);

                    if (workerAdvice.ReplicaCount < 3)
                    {
                        values.Add($"manager.minimumWorkers", "1");
                    }

                    if (workerAdvice.ReplicaCount < 3)
                    {
                        values.Add($"persistence.replicaCount", "1");
                    }

                    int i = 0;

                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystemDb, "true"))
                    {
                        values.Add($"tolerations[{i}].key", $"{taint.Key.Split("=")[0]}");
                        values.Add($"tolerations[{i}].effect", taint.Effect);
                        values.Add($"tolerations[{i}].operator", "Exists");
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "citus_postgresql", releaseName: "db-citus-postgresql", @namespace: KubeNamespaces.NeonSystem, values: values, progressMessage: "cluster database (citus)");

                    
                });

            await master.InvokeIdempotentAsync("setup/system-db-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "system database");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "db-citus-postgresql-manager", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonSystem, "db-citus-postgresql-master", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval),
                            k8s.WaitForStatefulSetAsync(KubeNamespaces.NeonSystem, "db-citus-postgresql-worker", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval)
                        });
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("setup/system-db-ready-to-go",
                   async () =>
                   {
                       // We need to generate new passwords for the system database users when finalizing
                       // a ready-to-go cluster so passwords will be unique across cluster deployments,
                       // otherwise the clusters would mall have the same password created when the 
                       // ready-to-go node image was created.

                       var serviceSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);

                       serviceSecret.StringData["password"] = NeonHelper.GetCryptoRandomPassword(20);
                       await k8s.ReplaceNamespacedSecretAsync(serviceSecret, serviceSecret.Name(), serviceSecret.Namespace());

                       var adminSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbAdminSecret, KubeNamespaces.NeonSystem);

                       adminSecret.StringData["password"] = NeonHelper.GetCryptoRandomPassword(20);
                       await k8s.ReplaceNamespacedSecretAsync(adminSecret, adminSecret.Name(), adminSecret.Namespace());

                       string stdOut = "";
                       string stdErr = "";

                       var handler = new ExecAsyncCallback(async (_stdIn, _stdOut, _stdError) =>
                       {
                           stdOut = Encoding.UTF8.GetString(await _stdOut.ReadToEndAsync());
                           stdErr = Encoding.UTF8.GetString(await _stdError.ReadToEndAsync());
                       });

                       var command = new string[]
                       {
                            "psql",
                            "--username", "neon_admin",
                            "-d", "harbor_core",
                            "-c", $@"""ALTER USER {KubeConst.NeonSystemDbServiceUser} WITH PASSWORD '{serviceSecret.StringData["password"]}'; """
                       };

                       var result = await k8s.NamespacedPodExecAsync(
                           name:              "db-citus-postgresql-master-0",
                           @namespace:        KubeNamespaces.NeonSystem,
                           container:         "citus",
                           command:           command,
                           tty:               true,
                           action:            handler,
                           cancellationToken: CancellationToken.None);

                       if (result != 0)
                       {
                           throw new Exception("Failed to reset harbor admin password.");
                       }

                       command = new string[]
                       {
                            "psql",
                            "--username", "neon_admin",
                            "-d", "harbor_core",
                            "-c", $@"""ALTER USER {KubeConst.NeonSystemDbAdminUser} WITH PASSWORD '{adminSecret.StringData["password"]}'; """
                       };

                       result = await k8s.NamespacedPodExecAsync(
                           name:              "db-citus-postgresql-master-0",
                           @namespace:        KubeNamespaces.NeonSystem,
                           container:         "citus",
                           command:           command,
                           tty:               true,
                           action:            handler,
                           cancellationToken: CancellationToken.None);

                       if (result != 0)
                       {
                           throw new Exception("Failed to reset harbor admin password.");
                       }

                   });
             }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs Keycloak.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallSsoAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);

            await InstallDexAsync(controller, master);
            await InstallNeonSsoProxyAsync(controller, master);
            await InstallGlauthAsync(controller, master);
            await InstallOauth2ProxyAsync(controller, master);
        }

        /// <summary>
        /// Installs Dex.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallDexAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Dex);

            var values = new Dictionary<string, object>();

            values.Add("cluster.name", cluster.Definition.Name);
            values.Add("cluster.domain", cluster.Definition.Domain);
            values.Add("ingress.subdomain", ClusterDomain.Sso);

            values.Add("secrets.grafana", NeonHelper.GetCryptoRandomPassword(32));
            values.Add("secrets.harbor", NeonHelper.GetCryptoRandomPassword(32));
            values.Add("secrets.kubernetes", NeonHelper.GetCryptoRandomPassword(32));
            values.Add("secrets.minio", NeonHelper.GetCryptoRandomPassword(32));
            values.Add("secrets.ldap", NeonHelper.GetCryptoRandomPassword(32));

            values.Add("config.issuer", $"https://{ClusterDomain.Sso}.{cluster.Definition.Domain}");

            // LDAP
            var baseDN = $@"dc={string.Join($@"\,dc=", cluster.Definition.Domain.Split('.'))}";
            values.Add("config.ldap.bindDN", $@"cn=serviceuser\,ou=admin\,{baseDN}");
            values.Add("config.ldap.bindPW", $@"cn=serviceuser\,ou=admin\,{baseDN}");
            values.Add("config.ldap.userSearch.baseDN", $@"cn=users\,{baseDN}");
            values.Add("config.ldap.groupSearch.baseDN", $@"ou=users\,{baseDN}");


            if (serviceAdvice.PodMemoryRequest.HasValue && serviceAdvice.PodMemoryLimit.HasValue)
            {
                values.Add($"resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest));
                values.Add($"resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit));
            }

            await master.InvokeIdempotentAsync("setup/dex-install",
                async () =>
                {
                    await master.InstallHelmChartAsync(controller, "dex", releaseName: "dex", @namespace: KubeNamespaces.NeonSystem, values: values, progressMessage: "dex");
                });

            await master.InvokeIdempotentAsync("setup/dex-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "neon-sso");

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "neon-sso-dex", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/dex-config",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "update dex configuration");

                        var configMap = NeonHelper.JsonDeserialize<dynamic>((await k8s.ReadNamespacedConfigMapAsync("neon-sso-dex", KubeNamespaces.NeonSystem)).Data["config.yaml"]);

                    });
            }
        }

        /// <summary>
        /// Installs Neon SSO Session Proxy.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallNeonSsoProxyAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.NeonSsoSessionProxy);

            var values = new Dictionary<string, object>();

            values.Add("cluster.name", cluster.Definition.Name);
            values.Add("cluster.domain", cluster.Definition.Domain);
            values.Add("ingress.subdomain", ClusterDomain.Sso);
            values.Add("secrets.cipherKey", AesCipher.GenerateKey(256));

            if (serviceAdvice.PodMemoryRequest.HasValue && serviceAdvice.PodMemoryLimit.HasValue)
            {
                values.Add($"resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest));
                values.Add($"resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit));
            }

            await master.InvokeIdempotentAsync("setup/neon-sso-session-proxy-install",
                async () =>
                {
                    await master.InstallHelmChartAsync(controller, "neon_sso_session_proxy", releaseName: "neon-sso-session-proxy", @namespace: KubeNamespaces.NeonSystem, values: values, progressMessage: "neon-sso-session-proxy");
                });

            await master.InvokeIdempotentAsync("setup/neon-sso-proxy-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "neon-sso-session-proxy");

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "neon-sso-session-proxy", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });

            if (readyToGoMode == ReadyToGoMode.Setup)
            {
                await master.InvokeIdempotentAsync("ready-to-go/neon-sso-ingress",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "ready-to-go", message: "update neon sso ingress");

                        var virtualService = ((JObject)await k8s.GetNamespacedCustomObjectAsync("networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "neon-sso-session-proxy")).ToObject<VirtualService>();

                        virtualService.Spec.Hosts =
                            new List<string>()
                            {
                                $"{ClusterDomain.Sso}.{cluster.Definition.Domain}"
                            };

                        await k8s.ReplaceNamespacedCustomObjectAsync(virtualService, "networking.istio.io", "v1alpha3", KubeNamespaces.NeonIngress, "virtualservices", "neon-sso-session-proxy");
                    });
            }
        }

        /// <summary>
        /// Installs Glauth.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallGlauthAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Glauth);

            var values = new Dictionary<string, object>();

            var secret = await k8s.ReadNamespacedSecretAsync(KubeConst.DexSecret, KubeNamespaces.NeonSystem);
            var ldapPassword = Encoding.UTF8.GetString(secret.Data["LDAP_SECRET"]);

            values.Add("cluster.name", cluster.Definition.Name);
            values.Add("cluster.domain", cluster.Definition.Domain);

            values.Add("config.backend.baseDN", $"dc={string.Join($@"\,dc=", cluster.Definition.Domain.Split('.'))}");

            values.Add("users.root.password", cluster.Definition.RootPassword ?? NeonHelper.GetCryptoRandomPassword(20));
            values.Add("users.serviceuser.password", ldapPassword);

            if (serviceAdvice.PodMemoryRequest.HasValue && serviceAdvice.PodMemoryLimit.HasValue)
            {
                values.Add($"resources.requests.memory", ToSiString(serviceAdvice.PodMemoryRequest));
                values.Add($"resources.limits.memory", ToSiString(serviceAdvice.PodMemoryLimit));
            }

            await master.InvokeIdempotentAsync("setup/glauth-install",
                async () =>
                {
                    await master.InstallHelmChartAsync(controller, "glauth", releaseName: "glauth", @namespace: KubeNamespaces.NeonSystem, values: values, progressMessage: "glauth");
                });

            await master.InvokeIdempotentAsync("setup/glauth-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "glauth");

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "neon-sso-dex", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Installs Oauth2-proxy.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallOauth2ProxyAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster       = controller.Get<ClusterProxy>(KubeSetupProperty.ClusterProxy);
            var k8s           = GetK8sClient(controller);
            var readyToGoMode = controller.Get<ReadyToGoMode>(KubeSetupProperty.ReadyToGoMode);
            var clusterAdvice = controller.Get<KubeClusterAdvice>(KubeSetupProperty.ClusterAdvice);
            var serviceAdvice = clusterAdvice.GetServiceAdvice(KubeClusterAdvice.Oauth2Proxy);


            await master.InvokeIdempotentAsync("setup/oauth2-proxy",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "oauth2 proxy");

                    var values = new Dictionary<string, object>();

                    values.Add("cluster.name", cluster.Definition.Name);
                    values.Add("cluster.domain", cluster.Definition.Domain);
                    values.Add("config.cookieSecret", NeonHelper.ToBase64(NeonHelper.GetCryptoRandomPassword(32)));
                    values.Add($"metrics.servicemonitor.interval", serviceAdvice.MetricsInterval ?? clusterAdvice.MetricsInterval);

                    await master.InstallHelmChartAsync(controller, "oauth2_proxy", releaseName: "neon-sso", @namespace: KubeNamespaces.NeonSystem, values: values, progressMessage: "neon-sso proxy");
                });

            await master.InvokeIdempotentAsync("setup/oauth2-proxy-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait for", message: "oauth2 proxy");

                    await k8s.WaitForDeploymentAsync(KubeNamespaces.NeonSystem, "neon-sso-oauth2-proxy", timeout: clusterOpTimeout, pollInterval: clusterOpPollInterval);
                });
        }

        /// <summary>
        /// Returns the Postgres connection string for the default database for the
        /// cluster's <see cref="KubeService.NeonSystemDb"/> deployment.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <returns>The connection string.</returns>
        public static async Task<string> GetSystemDatabaseConnectionStringAsync(ISetupController controller)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            var k8s        = GetK8sClient(controller);
            var secret     = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbAdminSecret, KubeNamespaces.NeonSystem);
            var username   = Encoding.UTF8.GetString(secret.Data["username"]);
            var password   = Encoding.UTF8.GetString(secret.Data["password"]);
            var dbHost     = KubeService.NeonSystemDb;
            var dbPort     = NetworkPorts.Postgres;
            var connString = $"Host={dbHost};Port={dbPort};Username={username};Password={password};Database=postgres";

            if (controller.Get<bool>(KubeSetupProperty.Redact, true))
            {
                controller.LogGlobal($"System database connection string: [{connString.Replace(password, "REDACTED")}]");
            }
            else
            {
                controller.LogGlobal($"System database connection string: [{connString}]");
            }

            return connString;
        }

        /// <summary>
        /// Creates a database within the <see cref="KubeService.NeonSystemDb"/> when the database doesn't already exist.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">Specifies the database name.</param>
        /// <param name="username">Specifies the database user name.</param>
        /// <param name="password">Optionally specifies the password.</param>
        /// <returns>The tracking <see cref="Task"/>The tracking <see cref="Task"/>.</returns>
        private static async Task CreateSystemDatabaseAsync(
            ISetupController controller, 
            NodeSshProxy<NodeDefinition> master, 
            string name, 
            string username, 
            string password = null)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            var k8s           = GetK8sClient(controller);
            var workers       = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem, labelSelector: "app=citus-postgresql-worker");
            var masters       = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem, labelSelector: "app=citus-postgresql-master");
            var secret        = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbAdminSecret, KubeNamespaces.NeonSystem);
            var adminUsername = Encoding.UTF8.GetString(secret.Data["username"]);
            var adminPassword = Encoding.UTF8.GetString(secret.Data["password"]);

            var selectDatabaseCommand = new string[]
                {
                    "/bin/bash",
                    "-c",
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/postgres -t -c ""SELECT 1 FROM pg_database WHERE datname = '{name}';"""
                };

            var selectRoleCommand = new string[]
                {
                    "/bin/bash",
                    "-c",
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/postgres -t -c ""SELECT 1 FROM pg_roles WHERE rolname='{username}'"""
                };

            var createDatabaseCommand = new string[]
                {
                    "/bin/bash", 
                    "-c", 
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/postgres -c ""CREATE DATABASE {name};"""
                };

            var createExtensionCommand = new string[]
                {
                    "/bin/bash", 
                    "-c", 
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/{name} -c ""CREATE EXTENSION citus;"""
                };

            ExecuteResponse result;

            foreach (var worker in workers.Items)
            {
                result = await k8s.NamespacedPodExecAsync(
                    name:       worker.Name(),
                    @namespace: worker.Namespace(),
                    container:  "citus",
                    command:    selectDatabaseCommand);

                if (result.OutputText.Trim() != "1")
                {
                    await k8s.NamespacedPodExecAsync(
                        name:       worker.Name(),
                        @namespace: worker.Namespace(),
                        container:  "citus",
                        command:    createDatabaseCommand);

                    await k8s.NamespacedPodExecAsync(
                        name:       worker.Name(),
                        @namespace: worker.Namespace(),
                        container:  "citus",
                        command:     createExtensionCommand);
                }
            }

            result = await k8s.NamespacedPodExecAsync(
                name:       masters.Items.First().Name(),
                @namespace: masters.Items.First().Namespace(),
                container:  "citus",
                command:    selectDatabaseCommand);

            if (result.OutputText.Trim() != "1")
            {
                await master.InvokeIdempotentAsync($"setup/citus-create-db-{name}",
                      async () =>
                      {
                          await k8s.NamespacedPodExecAsync(
                              name: masters.Items.First().Name(),
                              @namespace: masters.Items.First().Namespace(),
                              container: "citus",
                              command: createDatabaseCommand);
                      });

                await master.InvokeIdempotentAsync($"setup/citus-create-db-extension-{name}",
                      async () =>
                      {
                          await k8s.NamespacedPodExecAsync(
                              name: masters.Items.First().Name(),
                              @namespace: masters.Items.First().Namespace(),
                              container: "citus",
                              command: createExtensionCommand);
                      });
            }

            foreach (var worker in workers.Items)
            {
                await master.InvokeIdempotentAsync($"setup/citus-add-worker-{name}-{worker.Name()}",
                   async () =>
                   {
                       await k8s.NamespacedPodExecAsync(
                           name: masters.Items.First().Name(),
                           @namespace: masters.Items.First().Namespace(),
                           container: "citus",
                           command: new string[]
                           {
                                "/bin/bash",
                                "-c",
                                $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/{name} -c ""SELECT * from master_add_node('{worker.Name()}.db-citus-postgresql-worker', 5432);"""
                        });
                   });
            }

            result = await k8s.NamespacedPodExecAsync(
                name:       masters.Items.First().Name(),
                @namespace: masters.Items.First().Namespace(),
                container:  "citus",
                command:    selectRoleCommand);

            if (result.OutputText.Trim() != "1")
            {
                await k8s.NamespacedPodExecAsync(
                    name:       masters.Items.First().Name(),
                    @namespace: masters.Items.First().Namespace(),
                    container:  "citus",
                    command:    new string[]
                    {
                        "/bin/bash",
                        "-c",
                        $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/{name} -c ""CREATE USER {username} WITH PASSWORD '{password}';"""
                    });
            }

            await k8s.NamespacedPodExecAsync(
                name:       masters.Items.First().Name(),
                @namespace: masters.Items.First().Namespace(),
                container:  "citus",
                command:    new string[]
                {
                    "/bin/bash",
                    "-c",
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/postgres -c ""GRANT ALL PRIVILEGES ON DATABASE {name} TO {username};"""
                });

            await k8s.NamespacedPodExecAsync(
                name:       masters.Items.First().Name(),
                @namespace: masters.Items.First().Namespace(),
                container:  "citus",
                command:    new string[]
                {
                    "/bin/bash",
                    "-c",
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/postgres << SQL
SELECT run_command_on_workers($cmd$
  /* the command to run */
  DO
$do$
BEGIN
   IF NOT EXISTS (
      SELECT FROM pg_catalog.pg_roles  -- SELECT list can be empty for this
      WHERE  rolname = '{username}') THEN

      CREATE ROLE {username} LOGIN PASSWORD '{password}';
   END IF;
END
$do$;
$cmd$);
SQL"
                });

            await k8s.NamespacedPodExecAsync(
                name:       masters.Items.First().Name(),
                @namespace: masters.Items.First().Namespace(),
                container:  "citus",
                command:    new string[]
                {
                    "/bin/bash",
                    "-c",
                    $@"psql postgresql://{adminUsername}:{adminPassword}@localhost:{NetworkPorts.Postgres}/postgres << SQL
SELECT run_command_on_workers($cmd$
  /* the command to run */
  GRANT ALL PRIVILEGES ON DATABASE {name} TO {username}
$cmd$);
SQL"
                });
        }

        /// <summary>
        /// Deploys a Kubernetes job that runs Grafana setup.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task SetupGrafanaAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            var k8s = GetK8sClient(controller);

            // Create the Grafana database within the system database deployment.

            await master.InvokeIdempotentAsync("setup/grafana-db",
                async () =>
                {
                    master.Status = $"[{KubeService.NeonSystemDb}]: create: grafana database.";

                    var systemDbSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);

                    await CreateSystemDatabaseAsync(controller, master, "grafana", KubeConst.NeonSystemDbServiceUser, Encoding.UTF8.GetString(systemDbSecret.Data["password"]));
                });

            // Perform the Grafana Minio configuration.


            await master.InvokeIdempotentAsync("setup/minio-loki",
                async () =>
                {
                    master.Status = "create: grafana [loki] minio bucket";

                    await CreateMinioBucketAsync(controller, master, "loki");
                });

            await master.InvokeIdempotentAsync("setup/minio-cortex",
                async () =>
                {
                    master.Status = "create: grafana [cortex] minio bucket";

                    await CreateMinioBucketAsync(controller, master, "cortex");
                });

            await master.InvokeIdempotentAsync("setup/minio-alertmanager",
                async () =>
                {
                    master.Status = "create: grafana [alertmanager] minio bucket";

                    await CreateMinioBucketAsync(controller, master, "alertmanager");
                });

            await master.InvokeIdempotentAsync("setup/minio-cortex-ruler",
                async () =>
                {
                    master.Status = "create: grafana [cortex-ruler] minio bucket";

                    await CreateMinioBucketAsync(controller, master, "cortex-ruler");
                });

            await master.InvokeIdempotentAsync("setup/minio-tempo",
                async () =>
                {
                    master.Status = "create: grafana [tempo] minio bucket";

                    await CreateMinioBucketAsync(controller, master, "tempo");
                });
        }

        /// <summary>
        /// Creates a minio bucket by using the mc client on one of the minio server pods.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <param name="name">The new bucket name.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateMinioBucketAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master, string name)
        {
            var minioSecret = await GetK8sClient(controller).ReadNamespacedSecretAsync("minio", KubeNamespaces.NeonSystem);
            var accessKey = Encoding.UTF8.GetString(minioSecret.Data["accesskey"]);
            var secretKey = Encoding.UTF8.GetString(minioSecret.Data["secretkey"]);
            var k8s = GetK8sClient(controller);
            var minioPod = (await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem, labelSelector: "app.kubernetes.io/name=minio-operator")).Items.First();

            await master.InvokeIdempotentAsync($"setup/minio-bucket-{name}",
                async () =>
                {
                    await k8s.NamespacedPodExecAsync(
                        KubeNamespaces.NeonSystem,
                        minioPod.Name(),
                        "minio-operator",
                        new string[] {
                            "/bin/bash",
                            "-c",
                            $"/mc mb minio/{name}"
                        });
                });
        }

        /// <summary>
        /// Converts a <c>decimal</c> into a nice byte units string.
        /// </summary>
        /// <param name="value">The input value (or <c>null</c>).</param>
        /// <returns>The formatted output (or <c>null</c>).</returns>
        public static string ToSiString(decimal? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return new ResourceQuantity(value.GetValueOrDefault(), 0, ResourceQuantity.SuffixFormat.BinarySI).CanonicalizeString();
        }

        /// <summary>
        /// Converts a <c>double</c> value into a nice byte units string.
        /// </summary>
        /// <param name="value">The input value (or <c>null</c>).</param>
        /// <returns>The formatted output (or <c>null</c>).</returns>
        public static string ToSiString(double? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return new ResourceQuantity((decimal)value.GetValueOrDefault(), 0, ResourceQuantity.SuffixFormat.BinarySI).CanonicalizeString();
        }

        /// <summary>
        /// Returns the built-in cluster definition for a local neonDESKTOP cluster provisioned on WSL2.
        /// </summary>
        /// <returns>The cluster definition text.</returns>
        public static ClusterDefinition GetLocalWsl2ClusterDefintion()
        {
            var yaml =
@"
name: neon-desktop
datacenter: wsl2
environment: development
timeSources:
- pool.ntp.org
kubernetes:
  allowPodsOnMasters: true
hosting:
  environment: wsl2
nodes:
  master:
    role: master
";
            return ClusterDefinition.FromYaml(yaml);
        }
    }
}

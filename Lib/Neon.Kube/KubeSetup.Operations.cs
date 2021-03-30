﻿//-----------------------------------------------------------------------------
// FILE:	    KubeSetup.Operations.cs
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

using ICSharpCode.SharpZipLib.Zip;
using k8s;
using k8s.Models;
using Microsoft.Rest;
using Neon.Collections;
using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Retry;
using Neon.SSH;
using Neon.Tasks;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            controller.LogProgress(node, verb: "configure", message: "etc high availability");

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

            foreach (var master in cluster.Masters)
            {
                sbHaProxyConfig.Append(
$@"
    server {master.Name}         {master.Address}:30080");
            }

            sbHaProxyConfig.Append(
$@"
backend harbor_backend
    mode                    tcp
    balance                 roundrobin");

            foreach (var master in cluster.Masters)
            {
                sbHaProxyConfig.Append(
$@"
    server {master.Name}         {master.Address}:30443");
            }

            node.UploadText(" /etc/neonkube/neon-etcd-proxy.cfg", sbHaProxyConfig);

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
      image: {KubeConst.NeonContainerRegistery(controller)}/haproxy:{KubeVersions.HaproxyVersion}
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
        public static async Task LabelNodesAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/label-nodes",
                async () =>
                {
                    controller.LogProgress(master, verb: "label", message: "nodes");

                    try
                    {
                        // Generate a Bash script we'll submit to the first master
                        // that initializes the labels for all nodes.

                        var sbScript = new StringBuilder();
                        var sbArgs = new StringBuilder();

                        sbScript.AppendLineLinux("#!/bin/bash");

                        foreach (var node in cluster.Nodes)
                        {
                            var labelDefinitions = new List<string>();

                            if (node.Metadata.IsWorker)
                            {
                                // Kubernetes doesn't set the role for worker nodes so we'll do that here.

                                labelDefinitions.Add("kubernetes.io/role=worker");
                            }

                            labelDefinitions.Add($"{NodeLabels.LabelDatacenter}={GetLabelValue(cluster.Definition.Datacenter.ToLowerInvariant())}");
                            labelDefinitions.Add($"{NodeLabels.LabelEnvironment}={GetLabelValue(cluster.Definition.Environment.ToString().ToLowerInvariant())}");

                            foreach (var label in node.Metadata.Labels.All)
                            {
                                labelDefinitions.Add($"{label.Key}={GetLabelValue(label.Value)}");
                            }

                            sbArgs.Clear();

                            foreach (var label in labelDefinitions)
                            {
                                sbArgs.AppendWithSeparator(label);
                            }

                            sbScript.AppendLine();
                            sbScript.AppendLineLinux($"kubectl label nodes --overwrite {node.Name} {sbArgs}");

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

            var cluster      = controller.Get<ClusterProxy>(ClusterProxyProperty);
            var clusterLogin = controller.Get<ClusterLogin>(ClusterLoginProperty);
            var master       = cluster.FirstMaster;
            var debugMode    = controller.Get<bool>(KubeSetup.DebugModeProperty);

            cluster.ClearStatus();

            ConfigureKubernetes(controller, cluster.FirstMaster);
            ConfigureWorkstation(controller, master);
            ConnectCluster(controller);

            // We need to taint before deploying pods.

            await ConfigureMasterTaintsAsync(controller, master);

            // Run configuration tasks in parallel when not [--debug] mode.

            if (debugMode)
            {
                await TaintNodesAsync(controller);
                await LabelNodesAsync(controller, master);
                await NeonHelper.WaitAllAsync(await CreateNamespacesAsync(controller, master));
                await CreateRootUserAsync(controller, master);
                await InstallCalicoCniAsync(controller, master);
                await InstallIstioAsync(controller, master);
                await InstallMetricsServerAsync(controller, master);
            }
            else
            {
                var tasks = new List<Task>();

                tasks.Add(TaintNodesAsync(controller));
                tasks.Add(LabelNodesAsync(controller, master));
                tasks.AddRange(await CreateNamespacesAsync(controller, master));
                tasks.Add(CreateRootUserAsync(controller, master));
                tasks.Add(InstallCalicoCniAsync(controller, master));
                tasks.Add(InstallIstioAsync(controller, master));
                tasks.Add(InstallMetricsServerAsync(controller, master));

                await NeonHelper.WaitAllAsync(tasks);
            }

            if (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count() >= 3)
            {
                await InstallEtcdAsync(controller, master);
            }

            // Additional configuration.

            if (debugMode)
            {
                await InstallKialiAsync(controller, master);
                await InstallKubeDashboardAsync(controller, master);
                await InstallOpenEBSAsync(controller, master);
                await InstallPrometheusAsync(controller, master);
                await InstallSystemDbAsync(controller, master);
                await InstallMinioAsync(controller, master);
                await InstallClusterManagerAsync(controller, master);
                await InstallContainerRegistryAsync(controller, master);
                await NeonHelper.WaitAllAsync(await SetupMonitoringAsync(controller));
            }
            else
            {
                var tasks = new List<Task>();

                tasks.Add(InstallKialiAsync(controller, master));
                tasks.Add(InstallKubeDashboardAsync(controller, master));
                await InstallOpenEBSAsync(controller, master);
                await InstallPrometheusAsync(controller, master);
                await InstallSystemDbAsync(controller, master);
                await InstallMinioAsync(controller, master);
                tasks.Add(InstallClusterManagerAsync(controller, master));
                tasks.Add(InstallContainerRegistryAsync(controller, master));
                tasks.AddRange(await SetupMonitoringAsync(controller));

                await NeonHelper.WaitAllAsync(tasks);
            }
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

            var hostingEnvironment = controller.Get<HostingEnvironment>(KubeSetup.HostingEnvironmentProperty);
            var cluster            = controller.Get<ClusterProxy>(ClusterProxyProperty);
            var clusterLogin       = controller.Get<ClusterLogin>(ClusterLoginProperty);

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

                            // Configure the control plane's API server endpoint and initialize
                            // the certificate SAN names to include each master IP address as well
                            // as the HOSTNAME/ADDRESS of the API load balancer (if any).

                            controller.LogProgress(master, verb: "initialize", message: "cluster");

                            var controlPlaneEndpoint = $"kubernetes-masters:6442";
                            var sbCertSANs           = new StringBuilder();

                            if (hostingEnvironment == HostingEnvironment.Wsl2)
                            {
                                // Tweak the API server endpoint for WSL2.

                                controlPlaneEndpoint = $"localhost:{KubeNodePorts.KubeApiServer}";
                            }

                            if (!string.IsNullOrEmpty(cluster.Definition.Kubernetes.ApiLoadBalancer))
                            {
                                controlPlaneEndpoint = cluster.Definition.Kubernetes.ApiLoadBalancer;

                                var fields = cluster.Definition.Kubernetes.ApiLoadBalancer.Split(':');

                                sbCertSANs.AppendLine($"  - \"{fields[0]}\"");
                            }

                            foreach (var node in cluster.Masters)
                            {
                                sbCertSANs.AppendLine($"  - \"{node.Address}\"");
                            }

                            var kubeletFailSwapOnLine           = string.Empty;
                            var kubeInitgnoreSwapOnPreflightArg = string.Empty;

                            if (hostingEnvironment == HostingEnvironment.Wsl2)
                            {
                                // SWAP will be enabled by the default Microsoft WSL2 kernel which
                                // will cause Kubernetes to complain because this isn't a supported
                                // configuration.  We need to disable these error checks.

                                kubeletFailSwapOnLine = "failSwapOn: false";
                                kubeInitgnoreSwapOnPreflightArg = "--ignore-preflight-errors=Swap";
                            }

                            var clusterConfig = new StringBuilder();

                            clusterConfig.AppendLine(
$@"
apiVersion: kubeadm.k8s.io/v1beta2
kind: ClusterConfiguration
clusterName: {cluster.Name}
kubernetesVersion: ""v{KubeVersions.KubernetesVersion}""
imageRepository: ""{KubeConst.NeonContainerRegistery(controller)}""
apiServer:
  extraArgs:
    bind-address: 0.0.0.0
    logging-format: json
    default-not-ready-toleration-seconds: ""30"" # default 300
    default-unreachable-toleration-seconds: ""30"" #default  300
    allow-privileged: ""true""
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

                            if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2)
                            {
                                clusterConfig.AppendLine($@"
etcd:
  local:
    extraArgs:
        listen-peer-urls: https://127.0.0.1:2380
        listen-client-urls: https://127.0.0.1:2379
        advertise-client-urls: https://127.0.0.1:2379
        initial-advertise-peer-urls: https://127.0.0.1:2380
        initial-cluster=master-0: https://127.0.0.1:2380");
                            }

                            clusterConfig.AppendLine($@"
---
apiVersion: kubelet.config.k8s.io/v1beta1
kind: KubeletConfiguration
logging:
  format: json
nodeStatusReportFrequency: 4s
volumePluginDir: /var/lib/kubelet/volume-plugins
{kubeletFailSwapOnLine}
");

                            var kubeInitScript =
$@"
systemctl enable kubelet.service
kubeadm init --config cluster.yaml --ignore-preflight-errors=DirAvailable--etc-kubernetes-manifests
";
                            var response = master.SudoCommand(CommandBundle.FromScript(kubeInitScript).AddFile("cluster.yaml", clusterConfig.ToString()));

                            // Extract the cluster join command from the response.  We'll need this to join
                            // other nodes to the cluster.

                            var output = response.OutputText;
                            var pStart = output.IndexOf(joinCommandMarker, output.IndexOf(joinCommandMarker) + 1);

                            if (pStart == -1)
                            {
                                throw new KubeException("Cannot locate the [kubadm join ...] command in the [kubeadm init ...] response.");
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

                            controller.LogProgress(verb: "created", message: "cluster");
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

                    foreach (var master in cluster.Masters.Where(m => m != master))
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
                                                   $"{KubeConst.NeonContainerRegistery(controller)}/haproxy:neonkube-{KubeConst.NeonKubeVersion}"
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
                                    controller.LogProgress(master, verb: "configure", message: "kubernetes api server");

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
                                            $"{KubeConst.NeonContainerRegistery(controller)}/haproxy:neonkube-{KubeConst.NeonKubeVersion}"
                                        );

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

            firstMaster.InvokeIdempotent("setup/workstation",
                (Action)(() =>
                {
                    controller.LogProgress(firstMaster, verb: "configure", message: "workstation");

                    var cluster        = controller.Get<ClusterProxy>(ClusterProxyProperty);
                    var clusterLogin   = controller.Get<ClusterLogin>(ClusterLoginProperty);
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
                }));
        }

        /// <summary>
        /// Installs the Calico CNI.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static async Task InstallCalicoCniAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;

            await master.InvokeIdempotentAsync("setup/cni",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "calico");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("images.organization", KubeConst.NeonContainerRegistery(controller)));

                    if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2)
                    {
                        values.Add(new KeyValuePair<string, object>($"neonDesktop", $"true"));
                        values.Add(new KeyValuePair<string, object>($"kubernetes.service.host", $"localhost"));
                        values.Add(new KeyValuePair<string, object>($"kubernetes.service.port", KubeNodePorts.KubeApiServer));

                    }
                    await master.InstallHelmChartAsync(controller, "calico", releaseName: "calico", @namespace: "kube-system", values: values);

                    // Wait for Calico and CoreDNS pods to report that they're running.
                    // We're going to wait a maximum of 300 seconds.

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var pods = await GetK8sClient(controller).ListPodForAllNamespacesAsync();

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
                        timeout: clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);

                    await master.InvokeIdempotentAsync("setup/cni-ready",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "wait", message: "for calico");

                            var pods = await GetK8sClient(controller).CreateNamespacedPodAsync(
                                new V1Pod()
                                {
                                    Metadata = new V1ObjectMeta()
                                    {
                                        Name              = "dnsutils",
                                        NamespaceProperty = "default"
                                    },
                                    Spec = new V1PodSpec()
                                    {
                                        Containers = new List<V1Container>()
                                        {
                                            new V1Container()
                                            {
                                                Name            = "dnsutils",
                                                Image           = $"{KubeConst.NeonContainerRegistery(controller)}/kubernetes-e2e-test-images-dnsutils:1.3",
                                                Command         = new List<string>() {"sleep", "3600" },
                                                ImagePullPolicy = "IfNotPresent"
                                            }
                                        },
                                        RestartPolicy = "Always"
                                    }
                                }, "default");
                        });


                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var result = master.SudoCommand("kubectl exec -i -t dnsutils -- nslookup kubernetes.default", RunOptions.LogOutput);

                            if (result.Success)
                            {
                                await GetK8sClient(controller).DeleteNamespacedPodAsync("dnsutils", "default");
                                return await Task.FromResult(true);
                            }
                            else
                            {
                                master.SudoCommand("kubectl rollout restart --namespace kube-system deployment/coredns", RunOptions.LogOnErrorOnly);
                                await Task.Delay(5000);
                                return await Task.FromResult(false);
                            }
                        },
                        timeout: clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });
        }

        /// <summary>
        /// Configures pods to be schedule on masters when enabled.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static async Task ConfigureMasterTaintsAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;

            await master.InvokeIdempotentAsync("setup/kubernetes-master-taints",
                async () =>
                {
                    controller.LogProgress(master, verb: "configure", message: "master taints");

                    // The [kubectl taint] command looks like it can return a non-zero exit code.
                    // We'll ignore this.

                    if (cluster.Definition.Kubernetes.AllowPodsOnMasters.GetValueOrDefault())
                    {
                        master.SudoCommand(@"until [ `kubectl get nodes | grep ""NotReady"" | wc -l ` == ""0"" ]; do sleep 1; done", master.DefaultRunOptions & ~RunOptions.FaultOnError);
                        master.SudoCommand("kubectl taint nodes --all node-role.kubernetes.io/master-", master.DefaultRunOptions & ~RunOptions.FaultOnError);
                        master.SudoCommand(@"until [ `kubectl get nodes -o json | jq .items[].spec | grep ""NoSchedule"" | wc -l ` == ""0"" ]; do sleep 1; done", master.DefaultRunOptions & ~RunOptions.FaultOnError);
                    }
                    await Task.CompletedTask;
                });
        }

        /// <summary>
        /// Installs the Kubernetes Metrics Server service.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static async Task InstallMetricsServerAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = master.Cluster;

            await master.InvokeIdempotentAsync("setup/kubernetes-metrics-server",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "metrics-server");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "metrics_server", releaseName: "metrics-server", @namespace: "kube-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/kubernetes-metrics-server-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for metrics-server");

                    await WaitForDeploymentAsync(controller, "kube-system", "metrics-server");
                });
        }

        /// <summary>
        /// Installs Istio.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static async Task InstallIstioAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/istio",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "istio");

                    var istioScript0 =
$@"
tmp=$(mktemp -d /tmp/istioctl.XXXXXX)
cd ""$tmp"" || exit

curl -fsLO {KubeDownloads.IstioLinuxUri}

tar -xzf ""istioctl-{KubeVersions.IstioVersion}-linux-amd64.tar.gz""

# setup istioctl
cd ""$HOME"" || exit
mkdir -p "".istioctl/bin""
mv ""${{tmp}}/istioctl"" "".istioctl/bin/istioctl""
chmod +x "".istioctl/bin/istioctl""
rm -r ""${{tmp}}""

export PATH=$PATH:$HOME/.istioctl/bin

istioctl operator init --hub={KubeConst.NeonContainerRegistery(controller)} --tag={KubeVersions.IstioVersion}-distroless

kubectl create ns istio-system

cat <<EOF > istio-cni.yaml
apiVersion: install.istio.io/v1alpha1
kind: IstioOperator
metadata:
  namespace: istio-system
  name: istiocontrolplane
spec:
  hub: {KubeConst.NeonContainerRegistery(controller)}
  tag: {KubeVersions.IstioVersion}-distroless
  meshConfig:
    rootNamespace: istio-system
  components:
    ingressGateways:
    - name: istio-ingressgateway
      enabled: true
      k8s:
        overlays:
          - apiVersion: apps/v1
            kind: Deployment
            name: istio-ingressgateway
            patches:
              - path: kind
                value: DaemonSet
        service:
          ports:
          - name: http2
            protocol: TCP
            port: 80
            targetPort: 8080
            nodePort: 30080
          - name: https
            protocol: TCP
            port: 443
            targetPort: 8443
            nodePort: 30443
          - name: tls
            protocol: TCP
            port: 15443
            targetPort: 15443
            nodePort: 31922
        resources:
          requests:
            cpu: 10m
            memory: 64Mi
          limits:
            cpu: 2000m
            memory: 1024Mi
        strategy:
          rollingUpdate:
            maxSurge: ""100%""
            maxUnavailable: ""25%""
    cni:
      enabled: true
      namespace: kube-system
  values:
    global:
      logging:
        level: ""default:info""
      logAsJson: true
      imagePullPolicy: IfNotPresent
      proxy:
        resources:
          limits:
            cpu: 2000m
            memory: 1024Mi
          requests:
            cpu: 10m
            memory: 64Mi
      defaultNodeSelector: 
        neonkube.io/istio: true
      tracer:
        zipkin:
          address: neon-logging-jaeger-collector.monitoring.svc.cluster.local:9411
    pilot:
      traceSampling: 100
    meshConfig:
      accessLogFile: """"
      accessLogFormat: '{{   ""authority"": ""%REQ(:AUTHORITY)%"",   ""mode"": ""%PROTOCOL%"",   ""upstream_service_time"": ""%RESP(X-ENVOY-UPSTREAM-SERVICE-TIME)%"",   ""upstream_local_address"": ""%UPSTREAM_LOCAL_ADDRESS%"",   ""duration"": ""%DURATION%"",   ""request_duration"": ""%REQUEST_DURATION%"",   ""response_duration"": ""%RESPONSE_DURATION%"",   ""response_tx_duration"": ""%RESPONSE_TX_DURATION%"",   ""downstream_local_address"": ""%DOWNSTREAM_LOCAL_ADDRESS%"",   ""upstream_transport_failure_reason"": ""%UPSTREAM_TRANSPORT_FAILURE_REASON%"",   ""route_name"": ""%ROUTE_NAME%"",   ""response_code"": ""%RESPONSE_CODE%"",   ""response_code_details"": ""%RESPONSE_CODE_DETAILS%"",   ""user_agent"": ""%REQ(USER-AGENT)%"",   ""response_flags"": ""%RESPONSE_FLAGS%"",   ""start_time"": ""%START_TIME(%s.%6f)%"",   ""method"": ""%REQ(:METHOD)%"",   ""host"": ""%REQ(:Host)%"",   ""referer"": ""%REQ(:Referer)%"",   ""request_id"": ""%REQ(X-REQUEST-ID)%"",   ""forwarded_host"": ""%REQ(X-FORWARDED-HOST)%"",   ""forwarded_proto"": ""%REQ(X-FORWARDED-PROTO)%"",   ""upstream_host"": ""%UPSTREAM_HOST%"",   ""downstream_local_uri_san"": ""%DOWNSTREAM_LOCAL_URI_SAN%"",   ""downstream_peer_uri_san"": ""%DOWNSTREAM_PEER_URI_SAN%"",   ""downstream_local_subject"": ""%DOWNSTREAM_LOCAL_SUBJECT%"",   ""downstream_peer_subject"": ""%DOWNSTREAM_PEER_SUBJECT%"",   ""downstream_peer_issuer"": ""%DOWNSTREAM_PEER_ISSUER%"",   ""downstream_tls_session_id"": ""%DOWNSTREAM_TLS_SESSION_ID%"",   ""downstream_tls_cipher"": ""%DOWNSTREAM_TLS_CIPHER%"",   ""downstream_tls_version"": ""%DOWNSTREAM_TLS_VERSION%"",   ""downstream_peer_serial"": ""%DOWNSTREAM_PEER_SERIAL%"",   ""downstream_peer_cert"": ""%DOWNSTREAM_PEER_CERT%"",   ""client_ip"": ""%REQ(X-FORWARDED-FOR)%"",   ""requested_server_name"": ""%REQUESTED_SERVER_NAME%"",   ""bytes_received"": ""%BYTES_RECEIVED%"",   ""bytes_sent"": ""%BYTES_SENT%"",   ""upstream_cluster"": ""%UPSTREAM_CLUSTER%"",   ""downstream_remote_address"": ""%DOWNSTREAM_REMOTE_ADDRESS%"",   ""path"": ""%REQ(X-ENVOY-ORIGINAL-PATH?:PATH)%"" }}'
      accessLogEncoding: ""JSON""
    gateways:
      istio-ingressgateway:
        type: NodePort
        externalTrafficPolicy: Local
        sds:
          enabled: true
    prometheus:
      enabled: false
    grafana:
      enabled: false
    istiocoredns:
      enabled: true
      coreDNSImage: {KubeConst.NeonContainerRegistery(controller)}/coredns-coredns
      coreDNSTag: {KubeVersions.CoreDNSVersion}
      coreDNSPluginImage: {KubeConst.NeonContainerRegistery(controller)}/coredns-plugin:{KubeVersions.CoreDNSPluginVersion}
    cni:
      excludeNamespaces:
       - istio-system
       - kube-system
       - kube-node-lease
       - kube-public
       - jobs
      logLevel: info
EOF

istioctl install -f istio-cni.yaml
";
                    master.SudoCommand(CommandBundle.FromScript(istioScript0));
                    await Task.CompletedTask;
                });
        }

        /// <summary>
        /// Configures the root Kubernetes user.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static async Task CreateRootUserAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/root-user",
                async () =>
                {
                    controller.LogProgress(master, verb: "create", message: "kubernetes root user");

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
";
                    master.KubectlApply(userYaml);

                    await Task.CompletedTask;
                });
        }

        /// <summary>
        /// Configures the root Kubernetes user.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        public static async Task InstallKubeDashboardAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster      = controller.Get<ClusterProxy>(ClusterProxyProperty);
            var clusterLogin = controller.Get<ClusterLogin>(ClusterLoginProperty);

            master.InvokeIdempotent("setup/kube-dashboard",
                () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "kubernetes dashboard");

                    if (clusterLogin.DashboardCertificate != null)
                    {
                        controller.LogProgress(master, verb: "generate", message: "kubernetes dashboard certificate");

                        // We're going to tie the custom certificate to the IP addresses
                        // of the master nodes only.  This means that only these nodes
                        // can accept the traffic and also that we'd need to regenerate
                        // the certificate if we add/remove a master node.
                        //
                        // Here's the tracking task:
                        //
                        //      https://github.com/nforgeio/neonKUBE/issues/441

                        var masterAddresses = new List<string>();

                        foreach (var master in cluster.Masters)
                        {
                            masterAddresses.Add(master.Address.ToString());
                        }

                        var utcNow = DateTime.UtcNow;
                        var utc10Years = utcNow.AddYears(10);

                        var certificate = TlsCertificate.CreateSelfSigned(
                            hostnames: masterAddresses,
                            validDays: (int)(utc10Years - utcNow).TotalDays,
                            issuedBy:  "kubernetes-dashboard");

                        clusterLogin.DashboardCertificate = certificate.CombinedPem;
                        clusterLogin.Save();
                    }

                    // Deploy the dashboard.  Note that we need to insert the base-64
                    // encoded certificate and key PEM into the dashboard configuration
                    // YAML first.

                    controller.LogProgress(master, verb: "deploy", message: "kubernetes dashboard");

                    var dashboardYaml =
$@"# Copyright 2017 The Kubernetes Authors.
#
# Licensed under the Apache License, Version 2.0 (the """"License"""");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
# http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an """"AS IS"""" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.


apiVersion: v1
kind: Namespace
metadata:
  name: kubernetes-dashboard

---

apiVersion: v1
kind: ServiceAccount
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard

---

kind: Service
apiVersion: v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
spec:
  type: NodePort
  ports:
  - port: 443
    targetPort: 8443
    nodePort: {KubeNodePorts.KubeDashboard}
  selector:
    k8s-app: kubernetes-dashboard

---

apiVersion: v1
kind: Secret
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-certs
  namespace: kubernetes-dashboard
type: Opaque
data:
  cert.pem: $<CERTIFICATE>
  key.pem: $<PRIVATEKEY>

---

apiVersion: v1
kind: Secret
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-csrf
  namespace: kubernetes-dashboard
type: Opaque
data:
  csrf: """"

---

apiVersion: v1
kind: Secret
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-key-holder
  namespace: kubernetes-dashboard
type: Opaque

---

kind: ConfigMap
apiVersion: v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard-settings
  namespace: kubernetes-dashboard

---

kind: Role
apiVersion: rbac.authorization.k8s.io/v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
rules:
# Allow Dashboard to get, update and delete Dashboard exclusive secrets.
  - apiGroups: [""""]
    resources: [""secrets""]
    resourceNames: [""kubernetes-dashboard-key-holder"", ""kubernetes-dashboard-certs"", ""kubernetes-dashboard-csrf""]
    verbs: [""get"", ""update"", ""delete""]
# Allow Dashboard to get and update 'kubernetes-dashboard-settings' config map.
  - apiGroups: [""""]
    resources: [""configmaps""]
    resourceNames: [""kubernetes-dashboard-settings""]
    verbs: [""get"", ""update""]
# Allow Dashboard to get metrics.
  - apiGroups: [""""]
    resources: [""services""]
    resourceNames: [""heapster"", ""dashboard-metrics-scraper""]
    verbs: [""proxy""]
  - apiGroups: [""""]
    resources: [""services/proxy""]
    resourceNames: [""heapster"", ""http:heapster:"", ""https:heapster:"", ""dashboard-metrics-scraper"", ""http:dashboard-metrics-scraper""]
    verbs: [""get""]

---

kind: ClusterRole
apiVersion: rbac.authorization.k8s.io/v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
rules:
# Allow Metrics Scraper to get metrics from the Metrics server
  - apiGroups: [""metrics.k8s.io""]
    resources: [""pods"", ""nodes""]
    verbs: [""get"", ""list"", ""watch""]

---

apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: kubernetes-dashboard
subjects:
  - kind: ServiceAccount
    name: kubernetes-dashboard
    namespace: kubernetes-dashboard

---

apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: kubernetes-dashboard
subjects:
  - kind: ServiceAccount
    name: kubernetes-dashboard
    namespace: kubernetes-dashboard

---

kind: Deployment
apiVersion: apps/v1
metadata:
  labels:
    k8s-app: kubernetes-dashboard
  name: kubernetes-dashboard
  namespace: kubernetes-dashboard
spec:
  replicas: 1
  revisionHistoryLimit: 10
  selector:
    matchLabels:
      k8s-app: kubernetes-dashboard
  template:
    metadata:
      labels:
        k8s-app: kubernetes-dashboard
    spec:
      containers:
        - name: kubernetes-dashboard
          image: {KubeConst.NeonContainerRegistery(controller)}/kubernetesui-dashboard:v{KubeVersions.KubernetesDashboardVersion}
          imagePullPolicy: IfNotPresent
          ports:
            - containerPort: 8443
              protocol: TCP
          args:
            - --auto-generate-certificates=false
            - --tls-cert-file=cert.pem
            - --tls-key-file=key.pem
            - --namespace=kubernetes-dashboard
# Uncomment the following line to manually specify Kubernetes API server Host
# If not specified, Dashboard will attempt to auto discover the API server and connect
# to it. Uncomment only if the default does not work.
# - --apiserver-host=http://my-address:port
          volumeMounts:
            - name: kubernetes-dashboard-certs
              mountPath: /certs
# Create on-disk volume to store exec logs
            - mountPath: /tmp
              name: tmp-volume
          livenessProbe:
            httpGet:
              scheme: HTTPS
              path: /
              port: 8443
            initialDelaySeconds: 30
            timeoutSeconds: 30
      volumes:
        - name: kubernetes-dashboard-certs
          secret:
            secretName: kubernetes-dashboard-certs
        - name: tmp-volume
          emptyDir: {{}}
      serviceAccountName: kubernetes-dashboard
# Comment the following tolerations if Dashboard must not be deployed on master
      tolerations:
        - key: node-role.kubernetes.io/master
          effect: NoSchedule

---

kind: Service
apiVersion: v1
metadata:
  labels:
    k8s-app: dashboard-metrics-scraper
  name: dashboard-metrics-scraper
  namespace: kubernetes-dashboard
spec:
  ports:
    - port: 8000
      targetPort: 8000
  selector:
    k8s-app: dashboard-metrics-scraper

---

kind: Deployment
apiVersion: apps/v1
metadata:
  labels:
    k8s-app: dashboard-metrics-scraper
  name: dashboard-metrics-scraper
  namespace: kubernetes-dashboard
spec:
  replicas: 1
  revisionHistoryLimit: 10
  selector:
    matchLabels:
      k8s-app: dashboard-metrics-scraper
  template:
    metadata:
      labels:
        k8s-app: dashboard-metrics-scraper
    spec:
      containers:
        - name: dashboard-metrics-scraper
          image: {KubeConst.NeonContainerRegistery(controller)}/kubernetesui-metrics-scraper:{KubeVersions.KubernetesDashboardMetricsVersion}
          ports:
            - containerPort: 8000
              protocol: TCP
          livenessProbe:
            httpGet:
              scheme: HTTP
              path: /
              port: 8000
            initialDelaySeconds: 30
            timeoutSeconds: 30
          volumeMounts:
          - mountPath: /tmp
            name: tmp-volume
      serviceAccountName: kubernetes-dashboard
# Comment the following tolerations if Dashboard must not be deployed on master
      tolerations:
        - key: node-role.kubernetes.io/master
          effect: NoSchedule
      volumes:
        - name: tmp-volume
          emptyDir: {{}}
";

                    var dashboardCert = TlsCertificate.Parse(clusterLogin.DashboardCertificate);
                    var variables     = new Dictionary<string, string>();

                    variables.Add("CERTIFICATE", Convert.ToBase64String(Encoding.UTF8.GetBytes(dashboardCert.CertPemNormalized)));
                    variables.Add("PRIVATEKEY", Convert.ToBase64String(Encoding.UTF8.GetBytes(dashboardCert.KeyPemNormalized)));

                    using (var preprocessReader =
                        new PreprocessReader(dashboardYaml, variables)
                        {
                            StripComments = false,
                            ProcessStatements = false
                        }
                    )
                    {
                        dashboardYaml = preprocessReader.ReadToEnd();
                    }

                    master.KubectlApply(dashboardYaml);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Adds the node taints.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        public static async Task TaintNodesAsync(ISetupController controller)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);
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
        private static async Task InstallKialiAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/kiali",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "kaili");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("cr.spec.deployment.image_name", $"{KubeConst.NeonContainerRegistery(controller)}/kiali-kiali"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelIstio, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "kiali", releaseName: "kiali-operator", @namespace: "istio-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/kiali-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for kaili");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            WaitForDeploymentAsync(controller, "istio-system", "kiali-operator"),
                            WaitForDeploymentAsync(controller, "istio-system", "kiali")
                        });
                });

            await Task.CompletedTask;
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
        /// Installs OpenEBS.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallOpenEBSAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/openebs-all",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "openebs");

                    master.InvokeIdempotent("setup/openebs-namespace",
                        () =>
                        {
                            controller.LogProgress(master, verb: "deploy", message: "openebs-namespace");

                            GetK8sClient(controller).CreateNamespace(new V1Namespace()
                            {
                                Metadata = new V1ObjectMeta()
                                {
                                    Name = "openebs",
                                    Labels = new Dictionary<string, string>()
                                    {
                                        { "istio-injection", "disabled" }
                                    }
                                }
                            });
                        });

                    await master.InvokeIdempotentAsync("setup/openebs",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "deploy", message: "openebs");

                            var values = new List<KeyValuePair<string, object>>();

                            values.Add(new KeyValuePair<string, object>("apiserver.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("helper.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("localprovisioner.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("policies.monitoring.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("snapshotOperator.controller.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("snapshotOperator.provisioner.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("provisioner.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("ndm.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("ndmOperator.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("webhook.image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("jiva.image.organization", KubeConst.NeonContainerRegistery(controller)));

                            if (cluster.Definition.Workers.Count() >= 3)
                            {
                                var replicas = Math.Max(1, cluster.Definition.Workers.Count() / 3);

                                values.Add(new KeyValuePair<string, object>($"apiserver.replicas", replicas));
                                values.Add(new KeyValuePair<string, object>($"provisioner.replicas", replicas));
                                values.Add(new KeyValuePair<string, object>($"localprovisioner.replicas", replicas));
                                values.Add(new KeyValuePair<string, object>($"snapshotOperator.replicas", replicas));
                                values.Add(new KeyValuePair<string, object>($"ndmOperator.replicas", 1));
                                values.Add(new KeyValuePair<string, object>($"webhook.replicas", replicas));
                            }

                            await master.InstallHelmChartAsync(controller, "openebs", releaseName: "neon-storage-openebs", values: values, @namespace: "openebs");
                        });

                    if (cluster.HostingManager.HostingEnvironment != HostingEnvironment.Wsl2)
                    {
                        await master.InvokeIdempotentAsync("setup/openebs-cstor",
                            async () =>
                            {
                                controller.LogProgress(master, verb: "deploy", message: "openebs cstor");

                                var values = new List<KeyValuePair<string, object>>();

                                values.Add(new KeyValuePair<string, object>("cspcOperator.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("cspcOperator.poolManager.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("cspcOperator.cstorPool.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("cspcOperator.cstorPoolExporter.image.organization", KubeConst.NeonContainerRegistery(controller)));

                                values.Add(new KeyValuePair<string, object>("cvcOperator.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("cvcOperator.target.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("cvcOperator.volumeMgmt.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("cvcOperator.volumeExporter.image.organization", KubeConst.NeonContainerRegistery(controller)));

                                values.Add(new KeyValuePair<string, object>("csiController.resizer.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("csiController.snapshotter.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("csiController.snapshotController.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("csiController.attacher.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("csiController.provisioner.image.organization", KubeConst.NeonContainerRegistery(controller)));
                                values.Add(new KeyValuePair<string, object>("csiController.driverRegistrar.image.organization", KubeConst.NeonContainerRegistery(controller)));

                                values.Add(new KeyValuePair<string, object>("cstorCSIPlugin.image.organization", KubeConst.NeonContainerRegistery(controller)));

                                values.Add(new KeyValuePair<string, object>("csiNode.driverRegistrar.image.organization", KubeConst.NeonContainerRegistery(controller)));

                                values.Add(new KeyValuePair<string, object>("admissionServer.image.organization", KubeConst.NeonContainerRegistery(controller)));

                                await master.InstallHelmChartAsync(controller, "openebs_cstor_operator", releaseName: "neon-storage-openebs-cstor", values: values, @namespace: "openebs");
                            });
                    }

                    await master.InvokeIdempotentAsync("setup/openebs-ready",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "wait", message: "for openebs");

                            await NeonHelper.WaitAllAsync(
                                new List<Task>()
                                {
                                    WaitForDaemonsetAsync(controller, "openebs", "neon-storage-openebs-ndm"),
                                    WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-admission-server"),
                                    WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-apiserver"),
                                    WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-localpv-provisioner"),
                                    WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-ndm-operator"),
                                    WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-provisioner"),
                                    WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-snapshot-operator")
                                });
                        });

                    if (cluster.HostingManager.HostingEnvironment != HostingEnvironment.Wsl2)
                    {
                        controller.LogProgress(master, verb: "deploy", message: "openebs pool");

                        await master.InvokeIdempotentAsync("setup/openebs-pool",
                        async () =>
                        {
                            var cStorPoolCluster = new V1CStorPoolCluster()
                            {
                                Metadata = new V1ObjectMeta()
                                {
                                    Name              = "cspc-stripe",
                                    NamespaceProperty = "openebs"
                                },
                                Spec = new V1CStorPoolClusterSpec()
                                {
                                    Pools = new List<V1CStorPoolSpec>()
                                }
                            };

                            var blockDevices = ((JObject)await GetK8sClient(controller).ListNamespacedCustomObjectAsync("openebs.io", "v1alpha1", "openebs", "blockdevices")).ToObject<V1CStorBlockDeviceList>();

                            foreach (var n in cluster.Definition.Nodes)
                            {
                                if (blockDevices.Items.Any(bd => bd.Spec.NodeAttributes.GetValueOrDefault("nodeName") == n.Name))
                                {
                                    var pool = new V1CStorPoolSpec()
                                    {
                                        NodeSelector = new Dictionary<string, string>()
                                            {
                                               { "kubernetes.io/hostname", n.Name }
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
                                            Tolerations       = new List<V1Toleration>()
                                                {
                                                    { new V1Toleration() { Effect = "NoSchedule", OperatorProperty = "Exists" } },
                                                    { new V1Toleration() { Effect = "NoExecute", OperatorProperty = "Exists" } }
                                                }
                                        }
                                    };

                                    foreach (var bd in blockDevices.Items.Where(bd => bd.Spec.NodeAttributes.GetValueOrDefault("nodeName") == n.Name))
                                    {
                                        pool.DataRaidGroups.FirstOrDefault().BlockDevices.Add(
                                            new V1CStorBlockDeviceRef()
                                            {
                                                BlockDeviceName = bd.Metadata.Name
                                            });
                                    }

                                    cStorPoolCluster.Spec.Pools.Add(pool);
                                }
                            }

                            GetK8sClient(controller).CreateNamespacedCustomObject(cStorPoolCluster, "cstor.openebs.io", "v1", "openebs", "cstorpoolclusters");
                        });

                        await master.InvokeIdempotentAsync("setup/openebs-cstor-ready",
                            async () =>
                            {
                                controller.LogProgress(master, verb: "wait", message: "for openebs cstor");

                                await NeonHelper.WaitAllAsync(
                                    new List<Task>()
                                    {
                                        WaitForDaemonsetAsync(controller, "openebs", "neon-storage-openebs-cstor-csi-node"),
                                        WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-cstor-admission-server"),
                                        WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-cstor-cvc-operator"),
                                        WaitForDeploymentAsync(controller, "openebs", "neon-storage-openebs-cstor-cspc-operator")
                                    });
                            });

                        var replicas = 3;

                        if (cluster.Definition.Nodes.Where(n => n.OpenEBS).Count() < 3)
                        {
                            replicas = 1;
                        }

                        await CreateCstorStorageClass(controller, master, "openebs-cstor", replicaCount: replicas);
                        await CreateCstorStorageClass(controller, master, "openebs-cstor-unreplicated", replicaCount: 1);
                    }
                    else
                    {
                        await CreateHostPathStorageClass(controller, master, "openebs-cstor");
                        await CreateHostPathStorageClass(controller, master, "openebs-cstor-unreplicated");
                    }
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

            await master.InvokeIdempotentAsync($"setup/{name}-namespace",
                async () =>
                {
                    await GetK8sClient(controller).CreateNamespaceAsync(new V1Namespace()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name = name,
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
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task CreateHostPathStorageClass(
            ISetupController                controller,
            NodeSshProxy<NodeDefinition>    master,
            string                          name)
        {
            await master.InvokeIdempotentAsync($"setup/storage-class-hostpath-{name}",
                async () =>
                {
                    var storageClass = new V1StorageClass()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            Name        = name,
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

                    await GetK8sClient(controller).CreateStorageClassAsync(storageClass);
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
            await master.InvokeIdempotentAsync($"setup/storage-class-cstor-{name}",
                async () =>
                {
                    if (master.Cluster.Definition.Nodes.Where(n => n.OpenEBS).Count() < replicaCount)
                    {
                        replicaCount = master.Cluster.Definition.Nodes.Where(n => n.OpenEBS).Count();
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

                    await GetK8sClient(controller).CreateStorageClassAsync(storageClass);
                });
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

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/monitoring-etc",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "etc");

                    await CreateCstorStorageClass(controller, master, "neon-internal-etcd");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>($"replicas", cluster.Definition.Nodes.Count(n => n.Labels.Metrics == true).ToString()));

                    values.Add(new KeyValuePair<string, object>($"volumeClaimTemplate.resources.requests.storage", "1Gi"));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "etcd_cluster", releaseName: "neon-etcd", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/setup/monitoring-etc-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for etc (monitoring)");

                    await WaitForStatefulSetAsync(controller, "monitoring", "neon-system-etcd");
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs a Prometheus Operator to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallPrometheusAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/monitoring-prometheus",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "prometheus");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.tlsProxy.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.configmapReloadImage.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"prometheusOperator.prometheusConfigReloaderImage.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"global.kubeStateMetrics.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>($"global.nodeExporter.image.organization", KubeConst.NeonContainerRegistery(controller)));

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"prometheusOperator.admissionWebhooks.patch.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.tolerations[{i}].operator", "Exists"));

                        i++;
                    }

                    if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2
                        || cluster.Definition.Nodes.Count() == 1)
                    {
                        await CreateHostPathStorageClass(controller, master, "neon-internal-prometheus");

                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.storage.volumeClaimTemplate.spec.storageClassName", $"neon-internal-prometheus"));
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.storage.volumeClaimTemplate.spec.accessModes[0]", "ReadWriteOnce"));
                        values.Add(new KeyValuePair<string, object>($"alertmanager.alertmanagerSpec.storage.volumeClaimTemplate.spec.resources.requests.storage", $"5Gi"));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.storage.volumeClaimTemplate.spec.storageClassName", $"neon-internal-prometheus"));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.storage.volumeClaimTemplate.spec.accessModes[0]", "ReadWriteOnce"));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.storage.volumeClaimTemplate.spec.resources.requests.storage", $"5Gi"));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.remoteRead", null));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.remoteWrite", null));
                        values.Add(new KeyValuePair<string, object>($"prometheus.prometheusSpec.scrapeInterval", "2m"));
                    }

                    await master.InstallHelmChartAsync(controller, "prometheus_operator", releaseName: "neon-metrics-prometheus", @namespace: "monitoring", values: values);
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

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/monitoring-prometheus-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for prometheus");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            WaitForDeploymentAsync(controller, "monitoring", "neon-metrics-prometheus-ku-operator"),
                            WaitForDeploymentAsync(controller, "monitoring", "neon-metrics-prometheus-kube-state-metrics"),
                            WaitForDaemonsetAsync(controller, "monitoring", "neon-metrics-prometheus-prometheus-node-exporter"),
                            WaitForStatefulSetAsync(controller, "monitoring", "alertmanager-neon-metrics-prometheus-ku-alertmanager"),
                            WaitForStatefulSetAsync(controller, "monitoring", "prometheus-neon-metrics-prometheus-ku-prometheus")
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
                    var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);
                    var values  = new List<KeyValuePair<string, object>>();

                    if (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count() >= 3)
                    {
                        values.Add(new KeyValuePair<string, object>($"replicas", Math.Min(3, (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count()))));
                        values.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.lifecycler.ring.kvstore.store", "etcd"));
                        values.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.lifecycler.ring.kvstore.replication_factor", 3));
                    }

                    await master.InvokeIdempotentAsync("setup/monitoring-cortex",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "setup", message: "cortex");

                            if (cluster.Definition.Nodes.Any(n => n.Vm.GetMemory(cluster.Definition) < 4294965097L))
                            {
                                values.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.retain_period", $"120s"));
                                values.Add(new KeyValuePair<string, object>($"cortexConfig.ingester.metadata_retain_period", $"5m"));
                                values.Add(new KeyValuePair<string, object>($"cortexConfig.querier.batch_iterators", true));
                                values.Add(new KeyValuePair<string, object>($"cortexConfig.querier.max_samples", 10000000));
                                values.Add(new KeyValuePair<string, object>($"cortexConfig.table_manager.retention_period", "12h"));
                            }

                            int i = 0;
                            foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                            {
                                values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                                values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                                values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                                i++;
                            }

                            values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));

                            await master.InstallHelmChartAsync(controller, "cortex", releaseName: "neon-metrics-cortex", @namespace: "monitoring", values: values);
                        });

                    await master.InvokeIdempotentAsync("setup/monitoring-cortex-ready",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "wait", message: "for cortex");

                            await WaitForDeploymentAsync(controller, "monitoring", "neon-metrics-cortex");
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

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/monitoring-loki",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "loki");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));

                    if (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count() >= 3)
                    {
                        values.Add(new KeyValuePair<string, object>($"replicas", Math.Min(3, (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count()))));
                        values.Add(new KeyValuePair<string, object>($"config.ingester.lifecycler.ring.kvstore.store", "etcd"));
                        values.Add(new KeyValuePair<string, object>($"config.ingester.lifecycler.ring.kvstore.replication_factor", 3));
                    }

                    if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2
                        || cluster.Definition.Nodes.Count() == 1)
                    {
                        values.Add(new KeyValuePair<string, object>($"config.limits_config.reject_old_samples_max_age", "15m"));
                        values.Add(new KeyValuePair<string, object>($"resources.requests.memory", "64Mi"));
                        values.Add(new KeyValuePair<string, object>($"resources.limits.memory", "128Mi"));
                    }

                    await master.InstallHelmChartAsync(controller, "loki", releaseName: "neon-logs-loki", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-loki-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for loki");

                    await WaitForStatefulSetAsync(controller, "monitoring", "neon-logs-loki");
                });
        }

        /// <summary>
        /// Installs Promtail to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallPromtailAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            await master.InvokeIdempotentAsync("setup/monitoring-promtail",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "promtail");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));

                    if (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count() >= 3)
                    {
                        values.Add(new KeyValuePair<string, object>($"replicas", Math.Min(3, (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count()))));
                        values.Add(new KeyValuePair<string, object>($"config.ingester.lifecycler.ring.kvstore.store", "etcd"));
                        values.Add(new KeyValuePair<string, object>($"config.ingester.lifecycler.ring.kvstore.replication_factor", 3));
                    }

                    if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2
                        || cluster.Definition.Nodes.Count() == 1)
                    {
                        values.Add(new KeyValuePair<string, object>($"resources.requests.memory", "64Mi"));
                        values.Add(new KeyValuePair<string, object>($"resources.limits.memory", "128Mi"));
                    }

                    await master.InstallHelmChartAsync(controller, "promtail", releaseName: "neon-logs-promtail", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-promtail-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for promtail");

                    await WaitForDaemonsetAsync(controller, "monitoring", "neon-logs-promtail");
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

            await master.InvokeIdempotentAsync("setup/monitoring-grafana",
                    async () =>
                    {
                        controller.LogProgress(master, verb: "setup", message: "grafana");

                        var values = new List<KeyValuePair<string, object>>();

                        values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));
                        values.Add(new KeyValuePair<string, object>("downloadDashboardsImage.organization", KubeConst.NeonContainerRegistery(controller)));
                        values.Add(new KeyValuePair<string, object>("sidecar.image.organization", KubeConst.NeonContainerRegistery(controller)));

                        int i = 0;
                        foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelMetrics, "true"))
                        {
                            values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                            values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                            values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                            i++;
                        }

                        if (master.Cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2
                            || master.Cluster.Definition.Nodes.Count() == 1)
                        {
                            values.Add(new KeyValuePair<string, object>($"prometheusEndpoint", "http://prometheus-operated:9090"));
                            values.Add(new KeyValuePair<string, object>($"resources.requests.memory", "64Mi"));
                            values.Add(new KeyValuePair<string, object>($"resources.limits.memory", "128Mi"));
                        }

                        await master.InstallHelmChartAsync(controller, "grafana", releaseName: "neon-metrics-grafana", @namespace: "monitoring", values: values);
                    });

            await master.InvokeIdempotentAsync("setup/monitoring-grafana-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for grafana");

                    await WaitForDeploymentAsync(controller, "monitoring", "neon-metrics-grafana");
                });
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

            await master.InvokeIdempotentAsync("setup/minio-all",
                async () =>
                {
                    var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

                    if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2)
                    {
                        await CreateHostPathStorageClass(controller, master, "neon-internal-minio");
                    }
                    else
                    {
                        await CreateCstorStorageClass(controller, master, "neon-internal-minio");
                    }

                    await master.InvokeIdempotentAsync("setup/minio",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "deploy", message: "minio");

                            var values = new List<KeyValuePair<string, object>>();

                            values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("mcImage.organization", KubeConst.NeonContainerRegistery(controller)));
                            values.Add(new KeyValuePair<string, object>("helmKubectlJqImage.organization", KubeConst.NeonContainerRegistery(controller)));

                            if (cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count() >= 3)
                            {
                                var replicas = Math.Min(4, Math.Max(4, cluster.Definition.Nodes.Where(n => n.Labels.Metrics).Count() / 4));
                                values.Add(new KeyValuePair<string, object>($"replicas", replicas));
                                values.Add(new KeyValuePair<string, object>($"mode", "distributed"));
                            }

                            if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2
                                || cluster.Definition.Nodes.Count() == 1)
                            {
                                values.Add(new KeyValuePair<string, object>($"resources.requests.memory", "64Mi"));
                                values.Add(new KeyValuePair<string, object>($"resources.limits.memory", "256Mi"));
                            }

                            await master.InstallHelmChartAsync(controller, "minio", releaseName: "neon-system-minio", @namespace: "neon-system", values: values);
                        });

                    await master.InvokeIdempotentAsync("configure/minio-secret",
                        async () =>
                        {
                            controller.LogProgress(master, verb: "configure", message: "minio secret");

                            var secret = await GetK8sClient(controller).ReadNamespacedSecretAsync("neon-system-minio", "neon-system");

                            secret.Metadata.NamespaceProperty = "monitoring";

                            var monitoringSecret = new V1Secret()
                            {
                                Metadata = new V1ObjectMeta()
                                {
                                    Name = secret.Name()
                                },
                                Data = secret.Data,
                            };
                            await GetK8sClient(controller).CreateNamespacedSecretAsync(monitoringSecret, "monitoring");
                        });
                });
        }

        /// <summary>
        /// Installs an Neon Monitoring to the monitoring namespace.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task<List<Task>> SetupMonitoringAsync(ISetupController controller)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);
            var master  = cluster.FirstMaster;
            var tasks   = new List<Task>();

            controller.LogProgress(master, verb: "setup", message: "cluster metrics");

            tasks.Add(WaitForPrometheusAsync(controller, master));

            if (cluster.HostingManager.HostingEnvironment != HostingEnvironment.Wsl2)
            {
                tasks.Add(InstallCortexAsync(controller, master));
            }

            tasks.Add(InstallLokiAsync(controller, master));
            tasks.Add(InstallPromtailAsync(controller, master));
            tasks.Add(master.InstallHelmChartAsync(controller, "istio_prometheus", @namespace: "monitoring"));
            tasks.Add(InstallGrafanaAsync(controller, master));

            return tasks;
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

            await master.InvokeIdempotentAsync("setup/monitoring-jaeger",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "jaeger");

                    var values = new List<KeyValuePair<string, object>>();

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelLogs, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"ingester.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"ingester.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"ingester.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"agent.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"agent.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"agent.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"collector.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"collector.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"collector.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"query.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"query.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"query.tolerations[{i}].operator", "Exists"));

                        values.Add(new KeyValuePair<string, object>($"esIndexCleaner.tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"esIndexCleaner.tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"esIndexCleaner.tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "jaeger", releaseName: "neon-logs-jaeger", @namespace: "monitoring", values: values);
                });

            await master.InvokeIdempotentAsync("setup/monitoring-jaeger-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for jaeger");

                    await NeonHelper.WaitForAsync(
                        async () =>
                        {
                            var deployments = await GetK8sClient(controller).ListNamespacedDeploymentAsync("monitoring", labelSelector: "release=neon-logs-jaeger");
                            if (deployments == null || deployments.Items.Count < 2)
                            {
                                return false;
                            }

                            return deployments.Items.All(p => p.Status.AvailableReplicas == p.Spec.Replicas);
                        },
                        timeout:      clusterOpTimeout,
                        pollInterval: clusterOpRetryInterval);
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Installs a harbor container registry and required components.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallContainerRegistryAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            var adminPassword = NeonHelper.GetCryptoRandomPassword(20);

            await master.InvokeIdempotentAsync("setup/harbor-certificate",
                async () =>
                {
                    controller.LogProgress(master, verb: "configure", message: "harbor certificate");

                    await SyncContext.ClearAsync;

                    var cert = TlsCertificate.CreateSelfSigned(KubeConst.NeonContainerRegistery(controller), 4096);

                    var harborCert = new V1Secret()
                        {
                            Metadata = new V1ObjectMeta()
                            {
                                Name = "neon-registry-harbor-internal"
                            },
                            Type       = "Opaque",
                            StringData = new Dictionary<string, string>()
                            {
                                { "tls.crt", cert.CertPemNormalized },
                                { "tls.key", cert.KeyPemNormalized }
                            }
                        };

                        await GetK8sClient(controller).CreateNamespacedSecretAsync(harborCert, "neon-system");
                });

            await master.InvokeIdempotentAsync("setup/harbor-redis",
                async () =>
                {
                    await SyncContext.ClearAsync;

                    controller.LogProgress(master, verb: "setup", message: "harbor redis");

                    var values   = new List<KeyValuePair<string, object>>();
                    
                    values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));

                    var replicas = Math.Min(3, cluster.Definition.Masters.Count());

                    values.Add(new KeyValuePair<string, object>($"replicas", $"{replicas}"));
                    
                    if (replicas < 2)
                    {
                        values.Add(new KeyValuePair<string, object>($"hardAntiAffinity", false));
                        values.Add(new KeyValuePair<string, object>($"sentinel.quorum", 1));
                    }

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystemRegistry, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "redis_ha", releaseName: "neon-system-registry-redis", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/harbor-redis-ready",
                async () =>
                {
                    await SyncContext.ClearAsync;

                    controller.LogProgress(master, verb: "wait", message: "for harbor redis");

                    await WaitForStatefulSetAsync(controller, "neon-system", "neon-system-registry-redis-server");
                });

            await master.InvokeIdempotentAsync("setup/harbor",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "harbor");

                    var values = new List<KeyValuePair<string, object>>();

                    values.Add(new KeyValuePair<string, object>("nginx.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("portal.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("core.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("jobservice.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("registry.registry.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("registry.controller.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("chartmuseum.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("clair.clair.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("clair.adapter.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("trivy.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("notary.server.image.organization", KubeConst.NeonContainerRegistery(controller)));
                    values.Add(new KeyValuePair<string, object>("notary.signer.image.organization", KubeConst.NeonContainerRegistery(controller)));

                    values.Add(new KeyValuePair<string, object>($"harborAdminPassword", adminPassword));

                    if (cluster.Definition.Masters.Count() > 1)
                    {
                        var redisConnStr = string.Empty;
                        for (int i = 0; i < Math.Min(3, cluster.Definition.Masters.Count()); i++)
                        {
                            if (i > 0)
                            {
                                redisConnStr += "\\,";
                            }

                            redisConnStr += $"neon-system-registry-redis-announce-{i}:26379";
                        }

                        values.Add(new KeyValuePair<string, object>($"redis.external.addr", redisConnStr));
                        values.Add(new KeyValuePair<string, object>($"redis.external.sentinelMasterSet", "master"));
                    }

                    int j = 0;
                    foreach (var taint in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystemRegistry, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{j}].key", $"{taint.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{j}].effect", taint.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{j}].operator", "Exists"));
                        j++;
                    }

                    await master.InstallHelmChartAsync(controller, "harbor", releaseName: "neon-system-registry-harbor", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/harbor-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for harbor");

                    var startUtc = DateTime.UtcNow;

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-chartmuseum"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-clair"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-core"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-jobservice"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-notary-server"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-notary-signer"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-portal"),
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-registry-harbor-registry")
                        });
                });
        }

        /// <summary>
        /// Installs the Neon Cluster Manager.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="master">The master node where the operation will be performed.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task InstallClusterManagerAsync(ISetupController controller, NodeSshProxy<NodeDefinition> master)
        {
            await SyncContext.ClearAsync;

            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<ArgumentNullException>(master != null, nameof(master));

            await master.InvokeIdempotentAsync("setup/cluster-manager",
                async () =>
                {
                    controller.LogProgress(master, verb: "setup", message: "neon-cluster-manager");

                    var values = new List<KeyValuePair<string, object>>();
                    
                    values.Add(new KeyValuePair<string, object>("image.organization", KubeConst.NeonContainerRegistery(controller)));

                    await master.InstallHelmChartAsync(controller, "neon_cluster_manager", releaseName: "neon-cluster-manager", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/cluster-manager-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for neon-cluster-manager");

                    await WaitForDeploymentAsync(controller, "neon-system", "neon-cluster-manager");
                });
        }

        /// <summary>
        /// Installs the Neon Cluster Manager.
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

            tasks.Add(CreateNamespaceAsync(controller, master, "neon-system", true));
            tasks.Add(CreateNamespaceAsync(controller, master, "jobs", false));
            tasks.Add(CreateNamespaceAsync(controller, master, "monitoring", true));

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

            var cluster = controller.Get<ClusterProxy>(ClusterProxyProperty);

            var values = new List<KeyValuePair<string, object>>();

            values.Add(new KeyValuePair<string, object>($"image.organization", KubeConst.NeonContainerRegistery(controller)));
            values.Add(new KeyValuePair<string, object>($"busybox.image.organization", KubeConst.NeonContainerRegistery(controller)));
            values.Add(new KeyValuePair<string, object>($"prometheus.image.organization", KubeConst.NeonContainerRegistery(controller)));
            values.Add(new KeyValuePair<string, object>($"manager.image.organization", KubeConst.NeonContainerRegistery(controller)));

            if (cluster.HostingManager.HostingEnvironment == HostingEnvironment.Wsl2
                || cluster.Definition.Nodes.Count() == 1)
            {
                await CreateHostPathStorageClass(controller, master, "neon-internal-citus");
                values.Add(new KeyValuePair<string, object>($"worker.resources.requests.memory", "64Mi"));
                values.Add(new KeyValuePair<string, object>($"worker.resources.limits.memory", "128Mi"));
                values.Add(new KeyValuePair<string, object>($"master.resources.requests.memory", "64Mi"));
                values.Add(new KeyValuePair<string, object>($"master.resources.limits.memory", "128Mi"));
                values.Add(new KeyValuePair<string, object>($"manager.resources.requests.memory", "64Mi"));
                values.Add(new KeyValuePair<string, object>($"manager.resources.limits.memory", "128Mi"));
            }
            else
            {
                await CreateCstorStorageClass(controller, master, "neon-internal-citus");
            }

            await master.InvokeIdempotentAsync("setup/system-db",
                async () =>
                {
                    controller.LogProgress(master, verb: "deploy", message: "system database");

                    var replicas = Math.Max(1, cluster.Definition.Masters.Count() / 5);

                    values.Add(new KeyValuePair<string, object>($"master.replicas", replicas));
                    values.Add(new KeyValuePair<string, object>($"manager.replicas", replicas));
                    values.Add(new KeyValuePair<string, object>($"worker.replicas", replicas));

                    if (replicas < 3)
                    {
                        values.Add(new KeyValuePair<string, object>($"manager.minimumWorkers", "1"));
                    }

                    if (cluster.Definition.Nodes.Where(n => n.Labels.OpenEBS).Count() < 3)
                    {
                        values.Add(new KeyValuePair<string, object>($"persistence.replicaCount", "1"));
                    }

                    int i = 0;
                    foreach (var t in await GetTaintsAsync(controller, NodeLabels.LabelNeonSystemDb, "true"))
                    {
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].key", $"{t.Key.Split("=")[0]}"));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].effect", t.Effect));
                        values.Add(new KeyValuePair<string, object>($"tolerations[{i}].operator", "Exists"));
                        i++;
                    }

                    await master.InstallHelmChartAsync(controller, "citus_postgresql", releaseName: "neon-system-db", @namespace: "neon-system", values: values);
                });

            await master.InvokeIdempotentAsync("setup/system-db-ready",
                async () =>
                {
                    controller.LogProgress(master, verb: "wait", message: "for system database");

                    await NeonHelper.WaitAllAsync(
                        new List<Task>()
                        {
                            WaitForDeploymentAsync(controller, "neon-system", "neon-system-db-citus-postgresql-manager"),
                            WaitForStatefulSetAsync(controller, "neon-system", "neon-system-db-citus-postgresql-master"),
                            WaitForStatefulSetAsync(controller, "neon-system", "neon-system-db-citus-postgresql-worker")
                        });
                });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Waits for a service deployment to complete.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="namespace">The namespace.</param>
        /// <param name="name">The deployment name.</param>
        /// <param name="labelSelector">The optional label selector.</param>
        /// <param name="fieldSelector">The optional field selector.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task WaitForDeploymentAsync(
            ISetupController    controller, 
            string              @namespace, 
            string              name          = null, 
            string              labelSelector = null,
            string              fieldSelector = null)
        {
            Covenant.Requires<ArgumentException>(name != null || labelSelector != null || fieldSelector != null, "One of name, labelSelector or fieldSelector must be set,");

            if (!string.IsNullOrEmpty(name))
            {
                if (!string.IsNullOrEmpty(fieldSelector))
                {
                    fieldSelector += $",metadata.name={name}";
                }
                else
                {
                    fieldSelector = $"metadata.name={name}";
                }
            }

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    try
                    {
                        var deployments = await GetK8sClient(controller).ListNamespacedDeploymentAsync(@namespace, fieldSelector: fieldSelector, labelSelector: labelSelector);
                        if (deployments == null || deployments.Items.Count == 0)
                        {
                            return false;
                        }

                        return deployments.Items.All(d => d.Status.AvailableReplicas == d.Spec.Replicas);
                    }
                    catch
                    {
                        return false;
                    }
                            
                },
                timeout: clusterOpTimeout,
                pollInterval: clusterOpRetryInterval);
        }

        /// <summary>
        /// Waits for a stateful set deployment to complete.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="namespace">The namespace.</param>
        /// <param name="name">The deployment name.</param>
        /// <param name="labelSelector">The optional label selector.</param>
        /// <param name="fieldSelector">The optional field selector.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task WaitForStatefulSetAsync(
            ISetupController    controller,
            string              @namespace,
            string              name          = null,
            string              labelSelector = null,
            string              fieldSelector = null)
        {
            Covenant.Requires<ArgumentException>(name != null || labelSelector != null || fieldSelector != null, "One of name, labelSelector or fieldSelector must be set,");

            if (!string.IsNullOrEmpty(name))
            {
                if (!string.IsNullOrEmpty(fieldSelector))
                {
                    fieldSelector += $",metadata.name={name}";
                }
                else
                {
                    fieldSelector = $"metadata.name={name}";
                }
            }

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    try
                    {
                        var statefulsets = await GetK8sClient(controller).ListNamespacedStatefulSetAsync(@namespace, fieldSelector: fieldSelector, labelSelector: labelSelector);
                        if (statefulsets == null || statefulsets.Items.Count == 0)
                        {
                            return false;
                        }

                        return statefulsets.Items.All(s => s.Status.ReadyReplicas == s.Spec.Replicas);
                    }
                    catch
                    {
                        return false;
                    }
                },
                timeout: clusterOpTimeout,
                pollInterval: clusterOpRetryInterval);
        }

        /// <summary>
        /// Waits for a daemon set deployment to complete.
        /// </summary>
        /// <param name="controller">The setup controller.</param>
        /// <param name="namespace">The namespace.</param>
        /// <param name="name">The deployment name.</param>
        /// <param name="labelSelector">The optional label selector.</param>
        /// <param name="fieldSelector">The optional field selector.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public static async Task WaitForDaemonsetAsync(
            ISetupController    controller,
            string              @namespace,
            string              name          = null,
            string              labelSelector = null,
            string              fieldSelector = null)
        {
            Covenant.Requires<ArgumentException>(name != null || labelSelector != null || fieldSelector != null, "One of name, labelSelector or fieldSelector must be set,");

            if (!string.IsNullOrEmpty(name))
            {
                if (!string.IsNullOrEmpty(fieldSelector))
                {
                    fieldSelector += $",metadata.name={name}";
                }
                else
                {
                    fieldSelector = $"metadata.name={name}";
                }
            }
            await NeonHelper.WaitForAsync(
                async () =>
                {
                    try
                    {
                        var daemonsets = await GetK8sClient(controller).ListNamespacedDaemonSetAsync(@namespace, fieldSelector: fieldSelector, labelSelector: labelSelector);
                        if (daemonsets == null || daemonsets.Items.Count == 0)
                        {
                            return false;
                        }

                        return daemonsets.Items.All(d => d.Status.NumberAvailable == d.Status.DesiredNumberScheduled);
                    }
                    catch
                    {
                        return false;
                    }
                },
                timeout: clusterOpTimeout,
                pollInterval: clusterOpRetryInterval);
        }

        /// <summary>
        /// Returns the built-in cluster definition (as text) for a cluster provisioned on WSL2.
        /// </summary>
        /// <returns>The cluster definition text.</returns>
        public static string GetWsl2ClusterDefintion()
        {
            var definition =
@"
name: wsl2
datacenter: wsl2
environment: development
timeSources:
- pool.ntp.org
allowUnitTesting: true
kubernetes:
  allowPodsOnMasters: true
hosting:
  environment: wsl2
nodes:
  master-0:
    role: master
";
            return definition;
        }
    }
}

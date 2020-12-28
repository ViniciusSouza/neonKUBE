﻿//-----------------------------------------------------------------------------
// FILE:	    ContainerImages.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE LLC.  All rights reserved.
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;

namespace NeonImage
{
    /// <summary>
    /// Lists the container images to be prepositioned on node VM images.
    /// </summary>
    public static class ContainerImages
    {
        private const string requiredAsText = @"
bakdata/citus-k8s-membership-manager:v0.3
busybox:latest
calico/cni:v3.16.5
calico/kube-controllers:v3.16.5
calico/node:v3.16.5
calico/pod2daemon-flexvol:v3.16.5
citusdata/citus:9.4.0
coredns/coredns:1.6.2
curlimages/curl:7.70.0
docker.elastic.co/elasticsearch/elasticsearch:7.9.1
docker.elastic.co/kibana/kibana:7.9.1
goharbor/chartmuseum-photon:v2.1.1
goharbor/clair-adapter-photon:v2.1.1
goharbor/clair-photon:v2.1.1
goharbor/harbor-core:v2.1.1
goharbor/harbor-jobservice:v2.1.1
goharbor/harbor-portal:v2.1.1
goharbor/harbor-registryctl:v2.1.1
goharbor/notary-server-photon:v2.1.1
goharbor/notary-signer-photon:v2.1.1
goharbor/registry-photon:v2.1.1
grafana/grafana:7.1.5
istio/coredns-plugin:0.2-istio-1.1
istio/install-cni:1.7.1
istio/operator:1.7.1
istio/pilot:1.7.1
istio/proxyv2:1.7.1
jaegertracing/jaeger-agent:1.19.2
jaegertracing/jaeger-collector:1.19.2
jaegertracing/jaeger-query:1.19.2
jettech/kube-webhook-certgen:v1.0.0
k8s.gcr.io/coredns:1.7.0
k8s.gcr.io/etcd:3.4.13-0
k8s.gcr.io/kube-apiserver:v1.19.5
k8s.gcr.io/kube-controller-manager:v1.19.5
k8s.gcr.io/kube-proxy:v1.19.5
k8s.gcr.io/kube-scheduler:v1.19.5
k8s.gcr.io/pause:3.2
kiwigrid/k8s-sidecar:0.1.151
kubernetesui/dashboard:v2.0.4
kubernetesui/metrics-scraper:v1.0.1
ghcr.io/neonrelease-dev/haproxy:latest
ghcr.io/neonrelease-dev/neon-cluster-manager:latest
ghcr.io/neonrelease-dev/neon-log-collector:latest
ghcr.io/neonrelease-dev/neon-log-host:latest
openebs/admission-server:2.1.0
openebs/cspc-operator-amd64:2.1.0
openebs/cstor-csi-driver:2.1.0
openebs/cstor-istgt:2.1.0
openebs/cstor-pool:2.1.0
openebs/cstor-pool-manager-amd64:2.1.0
openebs/cstor-volume-manager-amd64:2.1.0
openebs/cstor-webhook-amd64:2.1.0
openebs/cvc-operator-amd64:2.1.0
openebs/linux-utils:2.1.0
openebs/m-apiserver:2.1.0
openebs/m-exporter:2.1.0
openebs/node-disk-manager:0.8.1
openebs/node-disk-operator:0.8.1
openebs/openebs-k8s-provisioner:2.1.0
openebs/provisioner-localpv:2.1.0
openebs/snapshot-controller:2.1.0
openebs/snapshot-provisioner:2.1.0
quay.io/coreos/configmap-reload:v0.0.1
quay.io/coreos/kube-state-metrics:v1.7.1
quay.io/coreos/prometheus-config-reloader:v0.32.0
quay.io/coreos/prometheus-operator:v0.32.0
quay.io/cortexproject/cortex:v1.5.0
quay.io/k8scsi/csi-attacher:v2.0.0
quay.io/k8scsi/csi-cluster-driver-registrar:v1.0.1
quay.io/k8scsi/csi-node-driver-registrar:v1.0.1
quay.io/k8scsi/csi-provisioner:v1.6.0
quay.io/k8scsi/csi-resizer:v0.4.0
quay.io/k8scsi/csi-snapshotter:v2.0.1
quay.io/k8scsi/snapshot-controller:v2.0.1
quay.io/kiali/kiali:v1.28.0
quay.io/kiali/kiali-operator:v1.27.0
quay.io/kubernetes_incubator/nfs-provisioner:v2.3.0
quay.io/prometheus/alertmanager:v0.19.0
quay.io/prometheus/node-exporter:v0.18.0
quay.io/prometheus/prometheus:v2.12.0
redis:6.0.7-alpine
squareup/ghostunnel:v1.4.1
wrouesnel/postgres_exporter:v0.5.1
";

        private const string requiredAsText2 = @"
calico/cni:v3.16.5
calico/node:v3.16.5
goharbor/chartmuseum-photon:v2.1.1
goharbor/clair-photon:v2.1.1
goharbor/harbor-core:v2.1.1
goharbor/harbor-jobservice:v2.1.1
goharbor/harbor-registryctl:v2.1.1
grafana/grafana:7.1.5
istio/coredns-plugin:0.2-istio-1.1
istio/install-cni:1.7.1
istio/operator:1.7.1
istio/pilot:1.7.1
istio/proxyv2:1.7.1
k8s.gcr.io/etcd:3.4.13-0
k8s.gcr.io/kube-apiserver:v1.19.5
k8s.gcr.io/kube-proxy:v1.19.5
kiwigrid/k8s-sidecar:0.1.151
kubernetesui/dashboard:v2.0.4
nkubedev/cluster-busybox:latest
nkubedev/cluster-calico-kube-controllers:latest
nkubedev/cluster-calico-pod2daemon-flexvol:latest
nkubedev/cluster-citus:latest
nkubedev/cluster-citus-membership-manager:latest
nkubedev/cluster-coredns-coredns:latest
nkubedev/cluster-curlimages-curl:latest
nkubedev/cluster-elasticsearch:latest
nkubedev/cluster-goharbor-clair-adapter-photon:latest
nkubedev/cluster-goharbor-harbor-portal:latest
nkubedev/cluster-goharbor-notary-server-photon:latest
nkubedev/cluster-goharbor-registry-photon:latest
nkubedev/cluster-goharbor-signer-photon:latest
nkubedev/cluster-jaegertracing-jaeger-agent:latest
nkubedev/cluster-jaegertracing-jaeger-collector:latest
nkubedev/cluster-jaegertracing-jaeger-query:latest
nkubedev/cluster-jettech-kube-webhook-certgen:latest
nkubedev/cluster-k8s-coredns:latest
nkubedev/cluster-k8s-kube-controller-manager:latest
nkubedev/cluster-k8s-kube-pause:latest
nkubedev/cluster-k8s-kube-scheduler:latest
nkubedev/cluster-kibana:latest
nkubedev/cluster-kubernetesui-metrics-scraper:latest
nkubedev/cluster-openebs-cspc-operator-amd64:latest
nkubedev/cluster-openebs-cstor-base:latest
nkubedev/cluster-openebs-cstor-istgt:latest
nkubedev/cluster-openebs-cstor-pool:latest
nkubedev/cluster-openebs-cstor-pool-manager:latest
nkubedev/cluster-openebs-cvc-operator-amd64:latest
nkubedev/cluster-openebs-linux-utils:latest
nkubedev/cluster-openebs-m-apiserver:latest
nkubedev/cluster-openebs-m-exporter:latest
nkubedev/cluster-ubuntu:latest
nkubedev/haproxy:latest
nkubedev/neon-cluster-manager:latest
nkubedev/neon-log-collector:latest
nkubedev/neon-log-host:latest
openebs/admission-server:2.1.0
openebs/cstor-csi-driver:2.1.0
openebs/cstor-volume-manager-amd64:2.1.0
openebs/cstor-webhook-amd64:2.1.0
openebs/node-disk-manager:0.8.1
openebs/node-disk-operator:0.8.1
openebs/openebs-k8s-provisioner:2.1.0
openebs/provisioner-localpv:2.1.0
openebs/snapshot-controller:2.1.0
openebs/snapshot-provisioner:2.1.0
quay.io/coreos/configmap-reload:v0.0.1
quay.io/coreos/kube-state-metrics:v1.7.1
quay.io/coreos/prometheus-config-reloader:v0.32.0
quay.io/coreos/prometheus-operator:v0.32.0
quay.io/cortexproject/cortex:v1.5.0
quay.io/k8scsi/csi-attacher:v2.0.0
quay.io/k8scsi/csi-cluster-driver-registrar:v1.0.1
quay.io/k8scsi/csi-node-driver-registrar:v1.0.1
quay.io/k8scsi/csi-provisioner:v1.6.0
quay.io/k8scsi/csi-resizer:v0.4.0
quay.io/k8scsi/csi-snapshotter:v2.0.1
quay.io/k8scsi/snapshot-controller:v2.0.1
quay.io/kiali/kiali:v1.28.0
quay.io/kiali/kiali-operator:v1.27.0
quay.io/kubernetes_incubator/nfs-provisioner:v2.3.0
quay.io/prometheus/alertmanager:v0.19.0
quay.io/prometheus/node-exporter:v0.18.0
quay.io/prometheus/prometheus:v2.12.0
redis:6.0.7-alpine
squareup/ghostunnel:v1.4.1
wrouesnel/postgres_exporter:v0.5.1
";

        /// <summary>
        /// Static constructor.
        /// </summary>
        static ContainerImages()
        {
            using (var reader = new StringReader(requiredAsText2))
            {
                foreach (var line in reader.Lines())
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Required.Add(line.Trim());
                    }
                }
            }
        }

        /// <summary>
        /// Returns the list of required container images.
        /// </summary>
        public static List<string> Required { get; } = new List<string>();
    }
}
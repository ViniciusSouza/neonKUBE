﻿//------------------------------------------------------------------------------
// FILE:         NeonKubeKv.cs
// CONTRIBUTOR:  Marcus Bowyer
// COPYRIGHT:    Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.Kube;
using Neon.Net;
using Neon.Retry;
using Neon.Service;
using Neon.Postgres;

using k8s;
using k8s.Models;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Npgsql;


namespace NeonKubeKv
{
    public partial class Service : NeonService
    {
        public string StateTable = "state";

        public KubernetesWithRetry k8s;
        public string DbConnectionString;

        // class fields
        private IWebHost webHost;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="serviceMap">Optionally specifies the service map.</param>
        public Service(string name, ServiceMap serviceMap = null)
            : base(name, serviceMap: serviceMap)
        {
            k8s = new KubernetesWithRetry(KubernetesClientConfiguration.BuildDefaultConfig());

            k8s.RetryPolicy = new ExponentialRetryPolicy(
                e => true,
                maxAttempts: int.MaxValue,
                initialRetryInterval: TimeSpan.FromSeconds(0.25),
                maxRetryInterval: TimeSpan.FromSeconds(10),
                timeout: TimeSpan.FromMinutes(5));
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected async override Task<int> OnRunAsync()
        {
            // Wait for Citus and make sure it's initialized.

            await WaitForCitusAsync();
            await InitializeDatabaseAsync();

            var endpoint = Description.Endpoints.Default;

            webHost = new WebHostBuilder()
                .UseStartup<KubeKv>()
                .UseKestrel(options => options.Listen(IPAddress.Any, endpoint.Uri.Port))
                .ConfigureServices(services => services.AddSingleton(typeof(Service), this))
                .Build();

            webHost.Start();

            // Let KubeService know that we're running.

            await SetRunningAsync();

            await Terminator.StopEvent.WaitAsync();

            return await Task.FromResult(0);
        }

        /// <summary>
        /// Gets a connection string for connecting to Citus.
        /// </summary>
        /// <param name="database"></param>
        /// <returns></returns>
        public async Task<string> GetConnectionStringAsync(string database = "postgres")
        {
            var secret   = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbAdminSecret, KubeNamespaces.NeonSystem);
            var username = Encoding.UTF8.GetString(secret.Data["username"]);
            var password = Encoding.UTF8.GetString(secret.Data["password"]);

            var dbHost = $"db-citus-postgresql.{KubeNamespaces.NeonSystem}";

            return $"Host={dbHost};Username={username};Password={password};Database={database}";
        }

        /// <summary>
        /// Gets the connection string used to connect to the neon-system database.
        /// </summary>
        /// <returns></returns>
        private async Task InitializeDatabaseAsync()
        {
            Log.LogInfo($"Connecting to citus...");

            var connString        = await GetConnectionStringAsync();
            var schemaDirectory   = Assembly.GetExecutingAssembly().GetResourceFileSystem("NeonKubeKv.Schema");
            var serviceUserSecret = await k8s.ReadNamespacedSecretAsync(KubeConst.NeonSystemDbServiceSecret, KubeNamespaces.NeonSystem);

            var variables = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                { "database", KubeConst.NeonKubeKvDatabase },
                { "service_user", Encoding.UTF8.GetString(serviceUserSecret.Data["username"]) },
                { "service_password", Encoding.UTF8.GetString(serviceUserSecret.Data["password"]) },
                { "state_table", StateTable }
            };

            await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
            {
                await conn.OpenAsync();

                using (var schemaManager = new SchemaManager(conn, KubeConst.NeonKubeKvDatabase, schemaDirectory, variables))
                {
                    var status = await schemaManager.GetStatusAsync();

                    switch (status.SchemaStatus)
                    {
                        case SchemaStatus.ExistsNoSchema:

                            Log.LogInfo($"[{KubeConst.NeonKubeKvDatabase}] database exists but is not initialized.");
                            break;

                        case SchemaStatus.ExistsWithSchema:

                            Log.LogInfo($"[{KubeConst.NeonKubeKvDatabase}] database exists with [version={status.Version}].");
                            break;

                        case SchemaStatus.NotFound:

                            Log.LogInfo($"[{KubeConst.NeonKubeKvDatabase}] database does not exist.");
                            await schemaManager.CreateDatabaseAsync();
                            break;

                        case SchemaStatus.Updating:

                            throw new SchemaManagerException($"[{KubeConst.NeonKubeKvDatabase}] database is currently being updated by [updater={status.Updater}].");

                        case SchemaStatus.UpgradeError:

                            throw new SchemaManagerException($"[{KubeConst.NeonKubeKvDatabase}] database is in an inconsistent state due to a previous update failure [updater={status.Updater}] [error={status.Error}].  This will require manual intervention.");

                        default:

                            throw new NotImplementedException();
                    }

                    var version = await schemaManager.UpgradeDatabaseAsync();

                    if (version == status.Version)
                    {
                        Log.LogInfo($"[{KubeConst.NeonKubeKvDatabase}] database is up to date at [version={version}]");
                    }
                    else
                    {
                        Log.LogInfo($"[{KubeConst.NeonKubeKvDatabase}] database is upgraded from [version={status.Version}] to [version={version}].");
                    }
                }
            }
        }

        /// <summary>
        /// Method to wait for neon-system Citus database to be ready.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task WaitForCitusAsync()
        {
            Log.LogInfo($"[{KubeNamespaces.NeonSystem}-db] Waiting for {KubeNamespaces.NeonSystem} database to be ready.");

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    var statefulsets = await k8s.ListNamespacedStatefulSetAsync(KubeNamespaces.NeonSystem, labelSelector: "release=db");

                    if (statefulsets == null || statefulsets.Items.Count < 2)
                    {
                        return false;
                    }

                    return statefulsets.Items.All(@set => @set.Status.ReadyReplicas == @set.Spec.Replicas);
                },
                timeout:      TimeSpan.FromMinutes(30),
                pollInterval: TimeSpan.FromSeconds(10));

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    var deployments = await k8s.ListNamespacedDeploymentAsync(KubeNamespaces.NeonSystem, labelSelector: "release=db");

                    if (deployments == null || deployments.Items.Count == 0)
                    {
                        return false;
                    }

                    return deployments.Items.All(deployment => deployment.Status.AvailableReplicas == deployment.Spec.Replicas);
                },
                timeout:      TimeSpan.FromMinutes(30),
                pollInterval: TimeSpan.FromSeconds(10));

            Log.LogInfo($"[{KubeNamespaces.NeonSystem}-db] {KubeNamespaces.NeonSystem} database is ready.");
        }

        /// <summary>
        /// Method to wait for neon-system Harbor registry to be ready.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task WaitForHarborAsync()
        {
            Log.LogInfo($"[{KubeNamespaces.NeonSystem}-registry] Waiting for registry to be ready.");

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    var deployments = (await k8s.ListNamespacedDeploymentAsync(KubeNamespaces.NeonSystem)).Items.Where(d => d.Metadata.Name.Contains("harbor"));

                    if (deployments == null)
                    {
                        return false;
                    }

                    return deployments.All(deployment => deployment.Status.AvailableReplicas == deployment.Spec.Replicas);
                },
                timeout:      TimeSpan.FromMinutes(30),
                pollInterval: TimeSpan.FromSeconds(10));

            Log.LogInfo($"[{KubeNamespaces.NeonSystem}-registry] Harbor is ready.");
        }

        /// <summary>
        /// Method to wait for neon-system minio to be ready.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        public async Task WaitForMinioAsync()
        {
            Log.LogInfo($"[{KubeNamespaces.NeonSystem}-minio] Waiting for Minio to be ready.");

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    var statefulsets = await k8s.ListNamespacedStatefulSetAsync(KubeNamespaces.NeonSystem, labelSelector: "app=minio");

                    return statefulsets.Items.All(@set => @set.Status.ReadyReplicas == @set.Spec.Replicas);
                },
                timeout:      TimeSpan.FromMinutes(30),
                pollInterval: TimeSpan.FromSeconds(10));

            Log.LogInfo($"[{KubeNamespaces.NeonSystem}-minio] Minio is ready.");
        }

        /// <summary>
        /// Deploys a Kubernetes job that runs Grafana setup.
        /// </summary>
        /// <returns></returns>
        public async Task SetupGrafanaAsync()
        {
            var connString = await GetConnectionStringAsync(KubeConst.NeonKubeKvDatabase);

            await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
            {
                conn.Open();
                await using (NpgsqlCommand cmd = new NpgsqlCommand($"SELECT value FROM {StateTable} WHERE key='{KubeConst.NeonJobSetupHarbor}'", conn))
                {
                    var result = (string)(await cmd.ExecuteScalarAsync());

                    if (result != "complete")
                    {
                        Log.LogInfo($"Grafana setup incomplete [{result}].");

                        var jobs = await k8s.ListNamespacedJobAsync(KubeNamespaces.NeonSystem);

                        if (!jobs.Items.Any(j => j.Metadata.Name == KubeConst.NeonJobSetupGrafana))
                        {
                            Log.LogInfo($"Creating Grafana setup job.");

                            await k8s.CreateNamespacedJobAsync(
                                new V1Job()
                                {
                                    Metadata = new V1ObjectMeta()
                                    {
                                        Name              = KubeConst.NeonJobSetupGrafana,
                                        NamespaceProperty = KubeNamespaces.NeonSystem
                                    },
                                    Spec = new V1JobSpec()
                                    {
                                        TtlSecondsAfterFinished = 100,
                                        Template = new V1PodTemplateSpec()
                                        {
                                            Spec = new V1PodSpec()
                                            {
                                                Containers = new List<V1Container>()
                                                {
                                                    new V1Container()
                                                    {
                                                        Name  = KubeConst.NeonJobSetupGrafana,
                                                        Image = $"{KubeConst.LocalClusterRegistry}/neon-setup-grafana:neonkube-{KubeConst.NeonKubeVersion}"
                                                    },
                                                },
                                                RestartPolicy      = "OnFailure",
                                                ServiceAccount     = NeonServices.ClusterOperator,
                                                ServiceAccountName = NeonServices.ClusterOperator
                                            },
                                        },
                                        BackoffLimit = 5,
                                    },
                                },
                                KubeNamespaces.NeonSystem);

                            Log.LogInfo($"Created Grafana setup job.");
                        }
                        else
                        {
                            Log.LogInfo($"Grafana setup job is running.");
                        }

                        var job = await k8s.ReadNamespacedJobAsync(KubeConst.NeonJobSetupGrafana, KubeNamespaces.NeonSystem);

                        while (job.Status.Succeeded < 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            job = await k8s.ReadNamespacedJobAsync(KubeConst.NeonJobSetupGrafana, KubeNamespaces.NeonSystem);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Deploys a Kubernetes job that runs Harbor setup.
        /// </summary>
        /// <returns></returns>
        public async Task SetupHarborAsync()
        {
            var connString = await GetConnectionStringAsync(KubeConst.NeonKubeKvDatabase);

            await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
            {
                conn.Open();
                await using (NpgsqlCommand cmd = new NpgsqlCommand($"SELECT value FROM {StateTable} WHERE key='{KubeConst.NeonJobSetupHarbor}'", conn))
                {
                    var result = (string)(await cmd.ExecuteScalarAsync());

                    if (result != "complete")
                    {
                        Log.LogInfo($"Harbor setup incomplete [{result}].");

                        var jobs = await k8s.ListNamespacedJobAsync(KubeNamespaces.NeonSystem);

                        if (!jobs.Items.Any(j => j.Metadata.Name == KubeConst.NeonJobSetupHarbor))
                        {
                            Log.LogInfo($"Creating Harbor setup job.");

                            await k8s.CreateNamespacedJobAsync(
                                new V1Job()
                                {
                                    Metadata = new V1ObjectMeta()
                                    {
                                        Name              = KubeConst.NeonJobSetupHarbor,
                                        NamespaceProperty = KubeNamespaces.NeonSystem
                                    },
                                    Spec = new V1JobSpec()
                                    {
                                        TtlSecondsAfterFinished = 100,
                                        Template = new V1PodTemplateSpec()
                                        {
                                            Spec = new V1PodSpec()
                                            {
                                                Containers = new List<V1Container>()
                                                {
                                                    new V1Container()
                                                    {
                                                        Name  = KubeConst.NeonJobSetupHarbor,
                                                        Image = $"{KubeConst.LocalClusterRegistry}/neon-setup-harbor:neonkube-{KubeConst.NeonKubeVersion}"
                                                    },
                                                },
                                                RestartPolicy      = "OnFailure",
                                                ServiceAccount     = NeonServices.ClusterOperator,
                                                ServiceAccountName = NeonServices.ClusterOperator
                                            },
                                        },
                                        BackoffLimit = 5,
                                    },
                                },
                                KubeNamespaces.NeonSystem);

                            Log.LogInfo($"Created Harbor setup job.");
                        }
                        else
                        {
                            Log.LogInfo($"Harbor setup job is running.");
                        }

                        var job = await k8s.ReadNamespacedJobAsync(KubeConst.NeonJobSetupHarbor, KubeNamespaces.NeonSystem);

                        while (job.Status.Succeeded < 1)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            job = await k8s.ReadNamespacedJobAsync(KubeConst.NeonJobSetupHarbor, KubeNamespaces.NeonSystem);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Responsible for making sure cluster container images are present in the local
        /// cluster registry.
        /// </summary>
        /// <returns></returns>
        public async Task CheckNodeImagesAsync()
        {
            var connString = await GetConnectionStringAsync(KubeConst.NeonKubeKvDatabase);

            await using (NpgsqlConnection conn = new NpgsqlConnection(connString))
            {
                conn.Open();
                await using (NpgsqlCommand cmd = new NpgsqlCommand($"SELECT value FROM {StateTable} WHERE key='{KubeConst.ClusterImagesLastChecked}'", conn))
                {
                    var result = (string)(await cmd.ExecuteScalarAsync());

                    if (result != null && DateTime.Parse(result) > DateTime.UtcNow.AddMinutes(-60))
                    {
                        return;
                    }
                }
            }
            
            await WaitForHarborAsync();

            // check busybox doesn't already exist

            var pods = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem);

            if (pods.Items.Any(p => p.Metadata.Name == "check-node-images-busybox"))
            {
                Log.LogInfo($"[check-node-images] Removing existing busybox pod.");
                
                await k8s.DeleteNamespacedPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem);

                await NeonHelper.WaitForAsync(
                    async () =>
                    {
                        pods = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem);

                        return !pods.Items.Any(p => p.Metadata.Name == "check-node-images-busybox");
                    }, 
                    timeout:      TimeSpan.FromSeconds(60),
                    pollInterval: TimeSpan.FromSeconds(2));
            }

            Log.LogInfo($"[check-node-images] Creating busybox pod.");

            var busybox = await k8s.CreateNamespacedPodAsync(
                new V1Pod()
                {
                    Metadata = new V1ObjectMeta()
                    {
                        Name              = "check-node-images-busybox",
                        NamespaceProperty = KubeNamespaces.NeonSystem
                    },
                    Spec = new V1PodSpec()
                    {
                        Tolerations = new List<V1Toleration>()
                        {
                            { new V1Toleration() { Effect = "NoSchedule", OperatorProperty = "Exists" } },
                            { new V1Toleration() { Effect = "NoExecute", OperatorProperty = "Exists" } }
                        },
                        HostNetwork = true,
                        HostPID     = true,
                        HostIPC     = true,
                        Volumes     = new List<V1Volume>()
                        {
                            new V1Volume()
                            {
                                Name = "noderoot",
                                HostPath = new V1HostPathVolumeSource()
                                {
                                    Path = "/",
                                }
                            }
                        },
                        Containers = new List<V1Container>()
                        {
                            new V1Container()
                            {
                                Name            = "check-node-images-busybox",
                                Image           = $"{KubeConst.LocalClusterRegistry}/busybox:{KubeVersions.BusyboxVersion}",
                                Command         = new List<string>() {"sleep", "infinity" },
                                ImagePullPolicy = "IfNotPresent",
                                SecurityContext = new V1SecurityContext()
                                {
                                    Privileged = true
                                },
                                VolumeMounts = new List<V1VolumeMount>()
                                {
                                    new V1VolumeMount()
                                    {
                                        Name = "noderoot",
                                        MountPath = "/host"
                                    }
                                }
                            }
                        },
                        RestartPolicy      = "Always",
                        ServiceAccount     = NeonServices.ClusterOperator,
                        ServiceAccountName = NeonServices.ClusterOperator
                    }
                }, KubeNamespaces.NeonSystem);

            await NeonHelper.WaitForAsync(
                async () =>
                {
                    pods = await k8s.ListNamespacedPodAsync(KubeNamespaces.NeonSystem);

                    return pods.Items.Any(p => p.Metadata.Name == "check-node-images-busybox");
                },
                timeout:      TimeSpan.FromSeconds(60),
                pollInterval: TimeSpan.FromSeconds(2));

            Log.LogInfo($"[check-node-images] Loading cluster manifest.");

            var clusterManifestJson = Program.Resources.GetFile("/cluster-manifest.json").ReadAllText();
            var clusterManifest     = NeonHelper.JsonDeserialize<ClusterManifest>(clusterManifestJson);

            Log.LogInfo($"[check-node-images] Getting images currently on node.");

            var crioOutput = NeonHelper.JsonDeserialize<dynamic>(await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"crictl images --output json",  retry: true));
            var nodeImages = ((IEnumerable<dynamic>)crioOutput.images).Select(image => image.repoTags).SelectMany(x => (JArray)x);

            foreach (var image in clusterManifest.ContainerImages)
            {
                if (nodeImages.Contains(image.InternalRef))
                {
                    Log.LogInfo($"[check-node-images] Image [{image.InternalRef}] exists. Pushing to registry.");
                    await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman push {image.InternalRef}", retry: true);
                } 
                else
                {
                    Log.LogInfo($"[check-node-images] Image [{image.InternalRef}] doesn't exist. Pulling from [{image.SourceRef}].");
                    await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman pull {image.SourceRef}", retry: true);
                    await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman tag {image.SourceRef} {image.InternalRef}");

                    Log.LogInfo($"[check-node-images] Pushing [{image.InternalRef}] to cluster registry.");
                    await ExecInPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem, $@"podman push {image.InternalRef}", retry: true);
                }

                await UpdateStatusAsync(DateTime.UtcNow.ToString());
            }

            Log.LogInfo($"[check-node-images] Removing busybox.");
            await k8s.DeleteNamespacedPodAsync("check-node-images-busybox", KubeNamespaces.NeonSystem);

            Log.LogInfo($"[check-node-images] Finished.");
        }

        /// <summary>
        /// Helper method for running node commands via a busybox container.
        /// </summary>
        /// <param name="podName"></param>
        /// <param name="namespace"></param>
        /// <param name="command"></param>
        /// <param name="containerName"></param>
        /// <param name="retry"></param>
        /// <returns>The command output as lines of text.</returns>
        public async Task<string> ExecInPodAsync(
            string      podName,
            string      @namespace,
            string      command,
            string      containerName = null,
            bool        retry         = false)
        {
            var podCommand = new string[]
            {
                "chroot",
                "/host",
                "/bin/bash",
                "-c",
                command
            };

            var pod = await k8s.ReadNamespacedPodAsync(podName, @namespace);

            if (string.IsNullOrEmpty(containerName))
            {
                containerName = pod.Spec.Containers.FirstOrDefault().Name;
            }

            string stdOut = "";
            string stdErr = "";

            var handler = new ExecAsyncCallback(async (_stdIn, _stdOut, _stdError) =>
            {
                stdOut = Encoding.UTF8.GetString(await _stdOut.ReadToEndAsync());
                stdErr = Encoding.UTF8.GetString(await _stdError.ReadToEndAsync());
            });

            var exitcode = await k8s.NamespacedPodExecAsync(podName, @namespace, containerName, podCommand, true, handler, CancellationToken.None);

            if (exitcode != 0)
            {
                throw new KubernetesException($@"{stdOut}

{stdErr}");
            }

            var result = new StringBuilder();
            foreach (var line in stdOut.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                result.AppendLine(line);
            }

            return result.ToString();
        }

        private async Task UpdateStatusAsync(string status)
        {
            await using var conn = new NpgsqlConnection(await GetConnectionStringAsync(KubeConst.NeonKubeKvDatabase));
            {
                await conn.OpenAsync();
                await using (var cmd = new NpgsqlCommand($@"
    INSERT
        INTO
        {StateTable} (KEY, value)
    VALUES (@k, @v) ON
    CONFLICT (KEY) DO
    UPDATE
    SET
        value = @v", conn))
                {
                    cmd.Parameters.AddWithValue("k", KubeConst.ClusterImagesLastChecked);
                    cmd.Parameters.AddWithValue("v", status);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
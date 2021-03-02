﻿//-----------------------------------------------------------------------------
// FILE:	    NodeSshProxy.BasePrepare.cs
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

// This file includes node configuration methods executed while preparing a
// node base image or performing live low-level initialization of a baremetal
// node.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Neon.Collections;
using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.SSH;
using Neon.Time;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Renci.SshNet;
using Renci.SshNet.Common;

namespace Neon.Kube 
{
    public partial class NodeSshProxy<TMetadata> : LinuxSshProxy<TMetadata>
        where TMetadata : class
    {
        /// <summary>
        /// Performs low-level initialization of a cluster.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="sshPassword">The current <b>sysadmin</b> password.</param>
        /// <param name="upgradeLinux">Optionally upgrade the node's Linux distribution.  This defaults to <c>false</c>.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseInitialize(HostingEnvironment hostingEnvironment, string sshPassword, bool upgradeLinux = false, Action<string> statusWriter = null)
        {
            Covenant.Requires<ArgumentException>(hostingEnvironment != HostingEnvironment.Unknown, nameof(hostingEnvironment));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(sshPassword), nameof(sshPassword));

            // $hack(jefflill):
            //
            // This method is going to be called for two different scenarios that will each
            // call for different logging mechanisms.
            //
            //      1. For the [neon prepare node-template] command, we're simply going 
            //         to write status to the console as lines via the [statusWriter].
            //
            //      2. For node preparation for cloud and bare metal clusters, we're
            //         going to set the node status and use the standard setup progress
            //         mechanism to display the status.
            //
            // [statusWriter] will be NULL for the second scenario so we'll call the log helper
            // method above which won't do anything.
            //
            // For scenario #1, there is no setup display mechanism, so updating node status
            // won't actually display anything, so we'll just set the status as well without
            // harming anything.

            // Wait for boot/connect.

            KubeHelper.WriteStatus(statusWriter, "Login", $"[{KubeConst.SysAdminUser}]");
            Status = $"login: [{KubeConst.SysAdminUser}]";

            WaitForBoot();
            VerifyNodeOS(statusWriter);
            BaseDisableSwap(hostingEnvironment, statusWriter);
            BaseInstallToolScripts(hostingEnvironment, statusWriter);
            BaseConfigureDebianFrontend(hostingEnvironment, statusWriter);
            BaseInstallPackages(hostingEnvironment, statusWriter);
            BaseConfigureApt(hostingEnvironment, statusWriter: statusWriter);
            BaseConfigureBashEnvironment(hostingEnvironment, statusWriter);
            BaseConfigureDnsIPv4Preference(hostingEnvironment, statusWriter);
            BaseRemoveSnap(hostingEnvironment, statusWriter);
            BaseRemovePackages(hostingEnvironment, statusWriter);
            BaseCreateKubeFolders(hostingEnvironment, statusWriter);

            if (upgradeLinux)
            {
                BaseUpgradeLinux(hostingEnvironment, statusWriter);
            }
        }

        /// <summary>
        /// Configures the Debian frontend terminal to non-interactive.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseConfigureDebianFrontend(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/debian-frontend",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Configure", $"Non-interactive terminal");
                    Status = $"login: interactive terminal";

                    // We need to append [DEBIAN_FRONTEND] to the [/etc/environment] file but
                    // we haven't installed [zip/unzip] yet so we can't use a command bundle.
                    // We'll just use [tee] in this case. 

                    SudoCommand("echo DEBIAN_FRONTEND=noninteractive | tee -a /etc/environment", RunOptions.Defaults | RunOptions.FaultOnError);
                    SudoCommand("echo 'debconf debconf/frontend select Noninteractive' | debconf-set-selections", RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Ubuntu defaults DNS to prefer IPv6 lookups over IPv4 which can cause
        /// performance problems.  This method reconfigures DNS to favor IPv4.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseConfigureDnsIPv4Preference(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/dns-ipv4",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Configure", $"DNS IPv4 preference");
                    Status = $"configure: ipv4 preference";

                    var script =
@"
#------------------------------------------------------------------------------
# We need to modify how [getaddressinfo] handles DNS lookups 
# so that IPv4 lookups are preferred over IPv6.  Ubuntu prefers
# IPv6 lookups by default.  This can cause performance problems 
# because in most situations right now, the server would be doing
# 2 DNS queries, one for AAAA (IPv6) which will nearly always 
# fail (at least until IPv6 is more prevalent) and then querying
# for the for A (IPv4) record.
#
# This can also cause issues when the server is behind a NAT.
# I ran into a situation where [apt-get update] started failing
# because one of the archives had an IPv6 address in addition to
# an IPv4.  Here's a note about this issue:
#
#       http://ubuntuforums.org/showthread.php?t=2282646
#
# We're going to uncomment the line below in [gai.conf] and
# change it to the following line to prefer IPv4.
#
#       #precedence ::ffff:0:0/96  10
#       precedence ::ffff:0:0/96  100
#
# Note that this does not completely prevent the resolver from
# returning IPv6 addresses.  You'll need to prvent this on an
# application by application basis, like using the [curl -4] option.

sed -i 's!^#precedence ::ffff:0:0/96  10$!precedence ::ffff:0:0/96  100!g' /etc/gai.conf
";
                    SudoCommand(CommandBundle.FromScript(script), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Configures the Debian frontend terminal to non-interactive.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseConfigureBashEnvironment(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/bash-environment",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Configure", $"Environment vars");
                    Status = $"configure: environment vars";

                    var script =
@"
echo '. /etc/environment' > /etc/profile.d/env.sh
";
                    SudoCommand(CommandBundle.FromScript(script), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Installs the required <b>base image</b> packages.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseInstallPackages(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/base-packages",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Install", "Base packages");
                    Status = "install: base packages";

                    // Install the packages.  Note that we haven't added our tool folder to the PATH 
                    // yet, so we'll use the fully qualified path to [safe-apt-get].

                    SudoCommand($"{KubeNodeFolders.Bin}/safe-apt-get update", RunOptions.Defaults | RunOptions.FaultOnError);
                    SudoCommand($"{KubeNodeFolders.Bin}/safe-apt-get install -yq apt-cacher-ng ntp secure-delete sysstat zip", RunOptions.Defaults | RunOptions.FaultOnError);

                    // I've seen some situations after a reboot where the machine complains about
                    // running out of entropy.  Apparently, modern CPUs have an instruction that
                    // returns cryptographically random data, but these CPUs weren't available
                    // until 2015 so our old HP SL 385 G10 XenServer machines won't support this.
                    //
                    // An reasonable alternative is [haveged]:
                    //   
                    //       https://wiki.archlinux.org/index.php/Haveged
                    //       https://www.digitalocean.com/community/tutorials/how-to-setup-additional-entropy-for-cloud-servers-using-haveged
                    //
                    // This article warns about using this though:
                    //
                    //       https://lwn.net/Articles/525459/
                    //
                    // The basic problem is that headless servers generally have very poor entropy
                    // sources because there's no mouse, keyboard, or active video card.  Outside
                    // of the new CPU instruction, the only sources are the HDD and network drivers.
                    // [haveged] works by timing running code at very high resolution and hoping to
                    // see execution time jitter and then use that as an entropy source.

                    SudoCommand($"{KubeNodeFolders.Bin}/safe-apt-get install -yq haveged", RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Updates Linux by applying just the outstanding security updates.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BasePatchLinux(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/patch-linux",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Install", "Linux security updates");
                    Status = "install: linux security updates";

                    PatchLinux(hostingEnvironment);
                });
        }

        /// <summary>
        /// Updates Linux by applying all outstanding package updates but without 
        /// upgrading the Linux distribution.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseUpdateLinux(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/update-linux",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Install", "All Linux updates");
                    Status = "install: all linux updates";

                    UpdateLinux(hostingEnvironment);
                });
        }

        /// <summary>
        /// Updates the Linux distribution.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseUpgradeLinux(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/upgrade-linux",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Upgrade", "Linux");
                    Status = "upgrade: linux";

                    UpgradeLinux(hostingEnvironment);
                });
        }

        /// <summary>
        /// Disables the Linux memory swap file.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseDisableSwap(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/swap-disable",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Disable", "Swap");
                    Status = "disable: swap";

                    // Disable SWAP by editing [/etc/fstab] to remove the [/swap.img...] line.

                    var sbFsTab = new StringBuilder();

                    using (var reader = new StringReader(DownloadText("/etc/fstab")))
                    {
                        foreach (var line in reader.Lines())
                        {
                            if (!line.Contains("/swap.img"))
                            {
                                sbFsTab.AppendLine(line);
                            }
                        }
                    }

                    UploadText("/etc/fstab", sbFsTab, permissions: "644", owner: "root:root");
                });
        }

        /// <summary>
        /// Installs hypervisor guest integration services.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseInstallGuestIntegrationServices(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            // This currently applies only to on-premise hypervisors.

            if (!KubeHelper.IsOnPremiseHypervisorEnvironment(hostingEnvironment))
            {
                return;
            }

            InvokeIdempotent("base/guest-integration",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Install", "Guest integration services");
                    Status = "install: guest integration services";

                    var guestServicesScript =
$@"#!/bin/bash
cat <<EOF >> /etc/initramfs-tools/modules
hv_vmbus
hv_storvsc
hv_blkvsc
hv_netvsc
EOF

{KubeNodeFolders.Bin}/safe-apt-get install -yq linux-virtual linux-cloud-tools-virtual linux-tools-virtual
update-initramfs -u
";
                    SudoCommand(CommandBundle.FromScript(guestServicesScript), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Disables DHCP.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseDisableDhcp(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            if (hostingEnvironment == HostingEnvironment.Wsl2)
            {
                return;
            }

            InvokeIdempotent("base/dhcp",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Disable", "DHCP");
                    Status = "disable: DHCP";

                    var initNetPlanScript =
$@"
rm /etc/netplan/*

cat <<EOF > /etc/netplan/no-dhcp.yaml
# This file is used to disable the network when a new VM is created 
# from a template is booted.  The [neon-init] service handles network
# provisioning in conjunction with the cluster prepare step.
#
# Cluster prepare inserts a virtual DVD disc with a script that
# handles the network configuration which [neon-init] will
# execute.

network:
  version: 2
  renderer: networkd
  ethernets:
    eth0:
      dhcp4: no
EOF
";
                    SudoCommand(CommandBundle.FromScript(initNetPlanScript), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Disables <b>cloud-init</b>.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseDisableCloudInit(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            // Do this only for non-cloud environments.

            if (KubeHelper.IsCloudEnvironment(hostingEnvironment))
            {
                return;
            }

            InvokeIdempotent("base/cloud-init",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Disable", "cloud-init");
                    Status = "disable: cloud-init";

                    var disableCloudInitScript =
$@"
touch /etc/cloud/cloud-init.disabled
";
                    SudoCommand(CommandBundle.FromScript(disableCloudInitScript), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Customizes the OpenSSH configuration on a 
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseConfigureOpenSsh(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/openssh",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Configure", "OpenSSH");
                    Status = "configure: openssh";

                    // Upload the OpenSSH server configuration and restart OpenSSH.

                    KubeHelper.WriteStatus(statusWriter, "Configure", "OpenSSH");
                    Status = "configure: OpenSSH";

                    UploadText("/etc/ssh/sshd_config", KubeHelper.OpenSshConfig);
                    SudoCommand("systemctl restart sshd", RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Removes any installed snaps as well as the entire snap infrastructure.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseRemoveSnap(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            // NOTE:
            //
            // The "base/remove-snap" action ID string below must match the string
            // used within [Wsl2Proxy.StartAsync()]

            InvokeIdempotent("base/remove-snap",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Remove", "Snap");
                    Status = "remove: snap";

                    var script =
@"
set -euo pipefail

apt-get purge snapd -yq

rm -rf ~/snap
rm -rf /var/cache/snapd
rm -rf /snap
";
                    SudoCommand(CommandBundle.FromScript(script), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Removes unneeded packages.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseRemovePackages(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/remove-packages",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Remove", "Unneeded packages");
                    Status = "remove: unneeded packages";

                    // Purge unneeded packages.

var removePackagesScript =
$@"
{KubeNodeFolders.Bin}/safe-apt-get purge -y \
    apt \
    aptitude \
    cloud-init \
    git git-man \
    iso-codes \
    locales \
    manpages man-db \
    python3-twisted \
    snapd \
    vim vim-runtime vim-tiny

{KubeNodeFolders.Bin}/safe-apt-get autoremove -y
";
                    SudoCommand(CommandBundle.FromScript(removePackagesScript), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Configures the APT package manager.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="packageManagerRetries">Optionally specifies the packager manager retries (defaults to <b>5</b>).</param>
        /// <param name="allowPackageManagerIPv6">Optionally prevent the package manager from using IPv6 (defaults to <c>false</c>.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseConfigureApt(HostingEnvironment hostingEnvironment, int packageManagerRetries = 5, bool allowPackageManagerIPv6 = false, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/apt",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Configure", "Package manager");
                    Status = "configure: package manager";

                    if (!allowPackageManagerIPv6)
                    {
                        // Restrict the [apt] package manager to using IPv4 to communicate
                        // with the package mirrors, since IPv6 doesn't work sometimes.

                        UploadText("/etc/apt/apt.conf.d/99-force-ipv4-transport", "Acquire::ForceIPv4 \"true\";");
                        SudoCommand("chmod 644 /etc/apt/apt.conf.d/99-force-ipv4-transport", RunOptions.Defaults | RunOptions.FaultOnError);
                    }

                    // Configure [apt] to retry.

                    UploadText("/etc/apt/apt.conf.d/99-retries", $"APT::Acquire::Retries \"{packageManagerRetries}\";");
                    SudoCommand("chmod 644 /etc/apt/apt.conf.d/99-retries", RunOptions.Defaults | RunOptions.FaultOnError);

                    // We're going to disable apt updating services so we can control when this happens.

                    var disableAptServices =
@"#------------------------------------------------------------------------------
# Disable the [apt-timer] and [apt-daily] services.  We're doing this 
# for two reasons:
#
#   1. These services interfere with with [apt-get] usage during
#      cluster setup and is also likely to interfere with end-user
#      configuration activities as well.
#
#   2. Automatic updates for production and even test clusters is
#      just not a great idea.  You just don't want a random update
#      applied in the middle of the night which might cause trouble.
#
# We're going to implement our own cluster updating machanism
# that will be smart enough to update the nodes such that the
# impact on cluster workloads will be limited.

systemctl stop apt-daily.timer
systemctl mask apt-daily.timer

systemctl stop apt-daily.service
systemctl mask apt-daily.service

# It may be possible for the auto updater to already be running so we'll
# wait here for it to release any lock files it holds.

while fuser /var/lib/dpkg/lock >/dev/null 2>&1; do
    sleep 1
done
";
                    SudoCommand(CommandBundle.FromScript(disableAptServices), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Installs the <b>neon-init</b> service which is a cloud-init like service we
        /// use to configure the network and credentials for VMs hosted in non-cloud
        /// hypervisors.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        /// <remarks>
        /// <para>
        /// Install and configure the [neon-init] service.  This is a simple script
        /// that is configured to run as a oneshot systemd service before networking is
        /// started.  This is currently used to configure the node's static IP address
        /// configuration on first boot, so we don't need to rely on DHCP (which may not
        /// be available in some environments).
        /// </para>
        /// <para>
        /// [neon-init] is intended to run the first time a node is booted after
        /// being created from a template.  It checks to see if a special ISO with a
        /// configuration script named [neon-init.sh] is inserted into the VMs DVD
        /// drive and when present, the script will be executed and the [/etc/neon-init/ready]
        /// file will be created to indicate that the service no longer needs to do this for
        /// subsequent reboots.
        /// </para>
        /// <note>
        /// The script won't create the [/etc/neon-init] when the script ISO doesn't exist 
        /// for debugging purposes.
        /// </note>
        /// </remarks>
        public void BaseInstallNeonInit(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/neon-init",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Install", "neon-init.service");
                    Status = "install: neon-init.service";

                    var neonNodePrepScript =
$@"# Ensure that the neon binary folder exists.

mkdir -p {KubeNodeFolders.Bin}

# Create the systemd unit file.

cat <<EOF > /etc/systemd/system/neon-init.service

[Unit]
Description=neonKUBE one-time node preparation service 
After=systemd-networkd.service

[Service]
Type=oneshot
ExecStart={KubeNodeFolders.Bin}/neon-init
RemainAfterExit=false
StandardOutput=journal+console

[Install]
WantedBy=multi-user.target
EOF

# Create the service script.

cat <<EOF > {KubeNodeFolders.Bin}/neon-init
#!/bin/bash
#------------------------------------------------------------------------------
# FILE:	        neon-init
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the ""License"");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
# http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an ""AS IS"" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# This script is run early during node boot before the netork is configured
# as a poor man's way for neonKUBE cluster setup to configure the network
# without requiring DHCP.  Here's how this works:
#
#       1. neonKUBE cluster setup creates a node VM from a template.
#
#       2. Setup creates a temporary ISO (DVD) image with a script named 
#          [neon-init.sh] on it and uploads this to the Hyper-V
#          or XenServer host machine.
#
#       3. Setup inserts the VFD into the VM's DVD drive and starts the VM.
#
#       4. The VM boots, eventually running this script (via the
#          [neon-init] service).
#
#       5. This script checks whether a DVD is present, mounts
#          it and checks it for the [neon-init.sh] script.
#
#       6. If the DVD and script file are present, this service will
#          execute the script via Bash, peforming any required custom setup.
#          Then this script creates the [/etc/neon-init] file which 
#          prevents the service from doing anything during subsequent node 
#          reboots.
#
#       7. The service just exits if the DVD and/or script file are 
#          not present.  This shouldn't happen in production but is useful
#          for script debugging.

# Run the prep script only once.

if [ -f /etc/neon-init/ready ] ; then
    echo ""INFO: Machine is already ready.""
    exit 0
fi

# Check for the DVD and prep script.

mkdir -p /media/neon-init

if [ ! $? ] ; then
    echo ""ERROR: Cannot create DVD mount point.""
    rm -rf /media/neon-init
    exit 1
fi

mount /dev/dvd /media/neon-init

if [ ! $? ] ; then
    echo ""WARNING: No DVD is present.""
    rm -rf /media/neon-init
    exit 0
fi

if [ ! -f /media/neon-init/neon-init.sh ] ; then
    echo ""WARNING: No [neon-init.sh] script is present on the DVD.""
    rm -rf /media/neon-init
    exit 0
fi

# The script file is present so execute it.  Note that we're
# passing the path where the DVD is mounted as a parameter.

echo ""INFO: Running [neon-init.sh]""
bash /media/neon-init/neon-init.sh /media/neon-init

# Unmount the DVD and cleanup.

echo ""INFO: Cleanup""
umount /media/neon-init
rm -rf /media/neon-init

# Disable [neon-init] the next time it starts.

mkdir -p /etc/neon-init
touch /etc/neon-init/ready
EOF

chmod 744 {KubeNodeFolders.Bin}/neon-init

# Configure [neon-init] to start at boot.

systemctl enable neon-init
systemctl daemon-reload
";
                    SudoCommand(CommandBundle.FromScript(neonNodePrepScript), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// Create the node folders required by neoneKUBE.
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseCreateKubeFolders(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/folders",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Create", "Cluster folders");
                    Status = "create: cluster folders";

                    var folderScript =
$@"
mkdir -p {KubeNodeFolders.Bin}
chmod 750 {KubeNodeFolders.Bin}

mkdir -p {KubeNodeFolders.Config}
chmod 750 {KubeNodeFolders.Config}

mkdir -p {KubeNodeFolders.Setup}
chmod 750 {KubeNodeFolders.Setup}

mkdir -p {KubeNodeFolders.Helm}
chmod 750 {KubeNodeFolders.Helm}

mkdir -p {KubeNodeFolders.State}
chmod 750 {KubeNodeFolders.State}

mkdir -p {KubeNodeFolders.State}/setup
chmod 750 {KubeNodeFolders.State}/setup
";
                    SudoCommand(CommandBundle.FromScript(folderScript), RunOptions.Defaults | RunOptions.FaultOnError);
                });
        }

        /// <summary>
        /// <para>
        /// Installs the tool scripts, making them executable.
        /// </para>
        /// <note>
        /// Any <b>".sh"</b> file extensions will be removed for ease-of-use.
        /// </note>
        /// </summary>
        /// <param name="hostingEnvironment">Specifies the hosting environment.</param>
        /// <param name="statusWriter">Optional status writer used when the method is not being executed within a setup controller.</param>
        public void BaseInstallToolScripts(HostingEnvironment hostingEnvironment, Action<string> statusWriter = null)
        {
            InvokeIdempotent("base/tool-scripts",
                () =>
                {
                    KubeHelper.WriteStatus(statusWriter, "Install", "Tools");
                    Status = "install: Tools";

                    // Upload any tool scripts to the neonKUBE bin folder, stripping
                    // the [*.sh] file type (if present) and then setting execute
                    // permissions.

                    var toolsFolder = KubeHelper.Resources.GetDirectory("/Tools");      // $hack(jefflill): https://github.com/nforgeio/neonKUBE/issues/1121

                    foreach (var file in toolsFolder.GetFiles())
                    {
                        var targetName = file.Name;

                        if (Path.GetExtension(targetName) == ".sh")
                        {
                            targetName = Path.GetFileNameWithoutExtension(targetName);
                        }

                        using (var toolStream = file.OpenStream())
                        {
                            UploadText(LinuxPath.Combine(KubeNodeFolders.Bin, targetName), toolStream, permissions: "744");
                        }
                    }
                });
        }
    }
}

﻿# neonKUBE Developer Setup

This page describes how to get started with neonKUBE development.

## Workstation Requirements

* Windows 10 Professional (64-bit) with at least 16GB RAM
* Virtualization capable workstation
* Visual Studio 2019 Edition (or better)
* Visual Studio Code

Note that the build environment currently assumes that only one Windows user will be acting as a developer on any given workstation.  Developers cannot share a machine and neonKUBE only builds on Windows at this time.

## Workstation Configuration

Follow the steps below to configure a development or test workstation:

1. Make sure that Windows is **fully updated**.

2. We highly recommend that you configure Windows to display hidden files:

    * Press the **Windows key** and run **File Explorer**
    * Click the **View** tab at the top.
    * Click the **Options** icon on the right and select **Change folder and search options**.
    * Click the **View** tab in the popup dialog.
    * Select the **Show hidden files, folders, and drives** radio button.
    * Uncheck the **Hide extensions for known types** check box.

3. Some versions of Skype listen for inbound connections on ports **80** and **443**.  This will interfere with services we'll want to test locally.  You need to disable this:

    * In Skype, select the **Tools/Options** menu.
    * Select the **Advanced/Connection** tab on the left.
    * **Uncheck**: Use **port 80 and 443** for additional incoming connections.

      ![Skype Connections](Images/Developer/SkypeConnections.png?raw=true)
    * **Restart Skype**

4. Ensure that Hyper-V is installed and enabled:

    * Run the following command in a **cmd** window to verify that your workstation is capable of virtualization and that it's enabled. You're looking for output like the image below:
      ```
      systeminfo
      ```
      ![Virtualization Info](Images/Developer/virtualization.png?raw=true)

      looking for a message saying that: **A hypervisor has been detected.**

    * Press the Windows key and enter: **windows features** and press ENTER.

    * Ensure that the check boxes highlighted in red below are checked:

    ![Hyper-V Features](Images/Developer/hyper-v.png?raw=true) 

    * Reboot your machine as required.

5. Uninstall **Powershell 6x** if installed.

6. Install the latest **64-bit** production release of PowerShell 7.1.3 (or greater) from [here](https://github.com/PowerShell/PowerShell/releases) (`PowerShell-#.#.#-win.x64.msi`)

7. Enable PowerShell script execution via (in a CMD window as administrator):
    ```
    powershell Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser
    ```

8. Enable **WSL2**:

    * Open a **pwsh** console **as administrator** and execute these commands:
    ```
    dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
    dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
    ```

    * Execute these Powershell commands in **pwsh** to install Ubuntu-20.04 on WSL2:
    ```
    Invoke-WebRequest https://neon-public.s3.us-west-2.amazonaws.com/downloads/ubuntu-20.04.tar -OutFile ubuntu.tar
    wsl --import Ubuntu-20.04 $env:USERPROFILE ubuntu.tar
    Remove-Item ubuntu.tar
    wsl --set-default-version 2
    wsl --set-default Ubuntu-20.04
    ```

9. Install **Docker for Windows (Stable)** from [here](https://www.docker.com/products/docker-desktop)

    * You'll need to create a DockerHub account if you don't already have one.
    * BuildKit causes random problems so be sure to disable it by setting **buildkit=false** in **Docker/Settings/Docker Engine**
    * Go to **Settings/Resources/NETWORK** and enable Manual DNS configuration (8.8.8.8)
	* Start a command window and use `docker login` to login using your GitHub credentials.

10. Install **Visual Studio 2022 Community 17.0.4+** from [here](https://visualstudio.microsoft.com/thank-you-downloading-visual-studio/?sku=Community&rel=16)

  * Select **all workloads** on the first panel
  * Select the **Individual Components** tab, search for "git" and check **Git for Windows**
  * Click **Install** (and take a coffee break)
  * Apply any pending **Visual Studio updates**
  * **Close** Visual Studio to install any updates
  * **NOTE:** You need sign into Visual Studio using a Windows account (like **sally@neonforge.com** for internal developers)

11. Create a **shortcut** for Visual Studio and configure it to run as **administrator**.  To build and run neonKUBE applications and services, **Visual Studio must be running with elevated privileges**.

12. Disable **Visual Studio YAML validation:**

    * Start Visual Studio
    * Select **Tools/Options...**
    * Navigate to **Text Editor/YAML/General**
    * Uncheck **YAML validation** at the top of the right panel

13. Configure Visual Studio Plain Text Editor:

    * Start Visual Studio
    * Select **Tools/Options...**
    * Navigate to **Text Editor/Plain Text/Tabs**
    * Set:
      * **Tab Size** = 4
      * **Indent Size** = 4
      * Select **Insert Spaces**

14. _(VS 2019 only):_ Disable **Python Import Warnings** via **Tools/Options** by unchecking this:

   ![System Tray](Images/Developer/PythonImports.png?raw=true)
  
15. Install some SDKs:

   * Install **.NET Framework 4.8 Developer Pack** from [here](https://dotnet.microsoft.com/download/thank-you/net48-developer-pack)
   * Install **.NET Core SDK 3.1.409** from [here](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-3.1.409-windows-x64-installer) (.NET SDK x64 installer)
   * Install **.NET 5.0 SDK 5.0.403** from [here](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-5.0.403-windows-x64-installer) (.NET SDK x64 installer)
   * Install **.NET 6.0 SDK 6.0.101** from [here](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-6.0.101-windows-x64-installer) (.NET SDK x64 installer)

16. **Clone** the [https://github.com/nforgeio/neonKUBE](https://github.com/nforgeio/neonKUBE) repository to your workstation:

    * **IMPORTANT:** All neonFORGE related repositories must be cloned within the same parent directory and their folder names must be the same as the repo names.
    * Create an individual GitHub account [here](https://github.com/join?source=header-home) if you don't already have one
    * Go to [GitHub](http://github.com) and log into your account
    * Go to the neonKUBE [repository](https://github.com/nforgeio/neonKUBE).
    * Click the *green* **Code** button and select **Open in Visual Studio**
    * A *Launch Application* dialog will appear.  Select **Microsoft Visual Studio Protocol Handler Selector** and click **Open Link**
    * Choose or enter the directory where the repository will be cloned.  This defaults to a user specific folder.  I typically change this to a global folder (like **C:\src**) to keep the file paths short.
    * Click **Clone**

17. Configure the build **environment variables**:

    * Open **File Explorer**
    * Navigate to the directory holding the cloned repository
    * **Right-click** on **buildenv.cmd** and then **Run as adminstrator**
    * Press ENTER to close the CMD window when the script is finished
  
17. Enable **WSL2**:

    * Open a **pwsh** console **as administrator** and execute these commands:
    ```
    dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
    dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
    ```

    * Execute these commands to install Ubuntu-20.04 on WSL2:
    ```
    Invoke-WebRequest https://neon-public.s3.us-west-2.amazonaws.com/vm-images/wsl2/virgin/virgin-ubuntu-20.04.20210206.wsl2.tar -OutFile ubuntu.tar
    wsl --import Ubuntu-20.04 "%USERPROFILE%\wsl-Ubuntu" ubuntu.tar
    Remove-Item ubuntu.tar
    wsl --set-default-version 2
    wsl --set-default Ubuntu-20.04
    ```

18. **Clone** the other neonFORGE repos to the same parent directory as **neonKUBE** without changing their folder names:

    * [https://github.com/nforgeio/temporal-samples](https://github.com/nforgeio/temporal-samples)
    * [https://github.com/nforgeio/cadence-samples](https://github.com/nforgeio/cadence-samples)
    * [https://github.com/nforgeio/nforgeio.github.io](https://github.com/nforgeio/nforgeio.github.io)

    You can do this manually or use the CMD script below: 

    ```
    cd "%NF_ROOT%\.."
    mkdir nforgeio.github.io
    git clone https://github.com/nforgeio/nforgeio.github.io.git

    cd "%NF_ROOT%\.."
    mkdir cadence-samples
    git clone https://github.com/nforgeio/cadence-samples.git

    cd "%NF_ROOT%\.."
    mkdir temporal-samples
    git clone https://github.com/nforgeio/temporal-samples.git
    ```

19. **Close** any running instances of **Visual Studio**

20. Install **7-Zip (32-bit)** (using the Windows *.msi* installer) from [here](http://www.7-zip.org/download.html)

21. Install **Cygwin - setup-x86-64.exe** (all packages and default path) from: [here](https://www.cygwin.com/setup-x86_64.exe)
    then run this in a command window to add it to the PATH:

    `%NF_TOOLBIN%\pathtool -dedup -system -add "C:\cygwin64\bin"`

22. Many server components are deployed to Linux, so you’ll need terminal and file management programs.  We’re currently 
    standardizing on **PuTTY** for the terminal and **WinSCP** for file transfer.  Install both programs to their default
    directories:

    * Install **WinSCP** from [here](http://winscp.net/eng/download.php) (I typically use the "Explorer" interface)
    * Install **PuTTY** from [here](https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html)
    * *Optional:* The default PuTTY color scheme sucks (dark blue on a black background doesn’t work for me).  You can update 
      the default scheme to Zenburn Light by **right-clicking** on the `$\External\zenburn-ligh-putty.reg` in **Windows Explorer** 
      and selecting **Merge**
    * WinSCP: Enable **hidden files**.  Start **WinSCP**, select **View/Preferences...**, and then click **Panels** on the left 
      and check **Show hidden files**:
    
      ![WinSCP Hidden Files](Images/Developer/WinSCPHiddenFiles.png?raw=true)

23. Install Visual Studio Code and GO (needed for the Cadence and Temporal proxy builds):

    * Install **Visual Studio Code** from [here](https://code.visualstudio.com/download)
    * Install **go1.17.2.windows-amd64.msi** from: [here](https://golang.org/dl/go1.17.2.windows-amd64.msi)

24. Confirm that the solution builds:

    * Restart **Visual Studio** as **administrator** (to pick up the new environment variables)
    * Open **$/neonKUBE.sln** (where **$** is the repo root directory)
    * Select **Build/Rebuild** Solution

25. *Optional:* Install **Notepad++** from [here](https://notepad-plus-plus.org/download)

26. *Optional:* Install **Postman** REST API tool from [here](https://www.getpostman.com/postman)

27. *Optional:* Install **Cmdr/Mini** command shell:

  * **IMPORTANT: Don't install the Full version** to avoid installing Linux command line tools that might conflict with the Cygwin tools installed earlier.
  * Download the ZIP archive from: [here](http://cmder.net/)
  * Unzip it into a new folder and then ensure that this folder is in your **PATH**.
  * Create a desktop shortcut if you wish and configure it to run as administrator.
  * Consider removing the alias definitions in `$\vendor\user_aliases.cmd.default` file so that commands like `ls` will work properly.  I deleted all lines beneath the first `@echo off`.
  * Run Cmdr to complete the installation.

28. *Optional:* Install the latest version of **XCP-ng Center** from [here](https://github.com/xcp-ng/xenadmin/releases) if you'll need to manage Virtual Machines hosted on XCP-ng.

29. *Optional:* Maintainers who will be publishing releases will need to:

    * **Download:** the latest recommended (at least **v5.8.0**) **nuget.exe** from [here](https://www.nuget.org/downloads) and put this somewhere in your `PATH`

    * Obtain a nuget API key from a maintainer and install the key on your workstation via:
	
	  `nuget SetApiKey YOUR-KEY`
	
    * **Install:** GitHub CLI (amd64) v1.9.2 or greater from: https://github.com/cli/cli/releases
    * **Close:** all Visual Studio instances.
    * **Install:** the HTML Help Compiler by running `$/External/htmlhelp.exe` with the default options.  You can ignore any message about a newer version already being installed.
    * **Unzip:** `$/External/SHFBInstaller_v2020.3.6.0.zip` to a temporary folder and run `SandcastleInstaller.exe`, then:
      * Click **Next** until you get to the **Sandcastle Help File Builder and Tools** page.
      * Click **Install SHFB**
	  * Click **Next** to the **Sandcastle Help File Builder Visual Studio Package** page.
	  * Click **Install Package**
	  * Click **Next**
	  * Click **Install Schemas**
      * Click **Next** until you get to the last page.
      * Click **Close** to close the SHFB installer.

30. *Optional:* Disable **Visual Studio Complete Line Intellicode**.  I (jefflill) personally find this distracting.  This blog post agrees and describes how to disable this:

    https://dotnetcoretutorials.com/2021/11/27/turning-off-visual-studio-2022-intellicode-complete-line-intellisense/

31. *Optional:* Install the [Bridge to Kubernetes](https://docs.microsoft.com/en-us/visualstudio/bridge/overview-bridge-to-kubernetes) Visual Studio extension to be able to debug service code from outside the cluster.

32. *Optional:* Create the **EDITOR** environment variable and point it to `C:\Program Files\Notepad++\notepad++.exe` or your favorite text editor executable.

33. *Optional:* Maintainers will need to install the **GitHub CLI** from here: https://cli.github.com/

34: *Optional:* Maintainers will need to **AWS client version 2** from: [here](https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2-windows.html)

35: *Optional:* Maintainers authorized to perform releases will need to follow the README.md instructions in the neonCLOUD repo to configure credentials for the GitHub Releases and the Container Registry.

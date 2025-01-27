﻿//-----------------------------------------------------------------------------
// FILE:	    PageBase.razor.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:   Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

using NeonDashboard.Shared;
using NeonDashboard.Shared.Components;

namespace NeonDashboard.Pages
{
    public partial class PageBase : ComponentBase
    {
        [Parameter]
        public string PageTitle { get; set; } = "NeonKUBE Dashboard";

        [Parameter]
        public string Description { get; set; } = "";


        public PageBase()
        {
        }

        public void Dispose()
        {
        }
    }
}
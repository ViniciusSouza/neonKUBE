﻿//-----------------------------------------------------------------------------
// FILE:	    Dashboard.razor.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:   Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NeonDashboard.Shared.Components
{
    public partial class Dashboard : ComponentBase, IDropUpItem, IDisposable
    {
        public Dashboard() { }
        public Dashboard(
            string id,
            string name,
            string uri, 
            string description = null)
        {
            Id          = id;
            Name        = name;
            Uri         = uri;
            Description = description;
        }

        [CascadingParameter(Name = "CurrentDashboard")]
        public string CurrentDashboard { get; set; }

        [Parameter]
        public string Id { get; set; } = null;

        [Parameter]
        public string Name { get; set; } = null;

        [Parameter]
        public string Uri { get; set; } = null;

        [Parameter]
        public string Description { get; set; }

        /// <summary>
        /// The height of the current frame. If this is the currently selected dashboard, return the max height.
        /// If this is not the current dashboard, then return 0 so that it is out of the way.
        /// </summary>
        public string Height
        {
            get
            {
                if (AppState?.CurrentDashboard == Id)
                {
                    return "100%";
                }
                return "0";
            }
        }

        /// <summary>
        /// The width of the current frame. If this is the currently selected dashboard, return the max width.
        /// If this is not the current dashboard, then return 0 so that it is out of the way.
        /// </summary>
        public string Width
        {
            get
            {
                if (AppState?.CurrentDashboard == Id)
                {
                    return "100%";
                }
                return "0";
            }
        }

        /// <inheritdoc />
        protected override void OnInitialized()
        {
            AppState.OnDashboardChange += StateHasChanged;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            AppState.OnDashboardChange -= StateHasChanged;
        }

        /// <inheritdoc />
        public string GetName()
        {
            return Name;
        }

        /// <inheritdoc />
        public string GetUri()
        {
            return Uri;
        }

        /// <inheritdoc />
        public string GetId()
        {
            return Id;
        }
    }
}
// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved


using Kona.Infrastructure.Flyouts;
using Kona.Infrastructure.Interfaces;

namespace Kona.AWShopper.Views
{
    public sealed partial class SignOutFlyout : FlyoutView
    {
        public SignOutFlyout(ISearchPaneService searchPaneService)
            : base(StandardFlyoutSize.Narrow, searchPaneService)
        {
            this.InitializeComponent();
        }
    }
}

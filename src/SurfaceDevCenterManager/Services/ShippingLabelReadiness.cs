/*++
    Copyright (c) Microsoft Corporation and Contributors. All rights reserved.

    Licensed under the MIT license. See LICENSE file in the project root for full license information.
--*/

using Microsoft.Devices.HardwareDevCenterManager.DevCenterApi;

namespace SurfaceDevCenterManager.Services;

/// <summary>
///     Shipping-label wait rules. <c>workflowStatus.state</c> is the state of
///     <c>workflowStatus.currentStep</c>, so <c>completed</c> on an intermediate step is not a
///     finished label. <c>published</c> is the documented success signal for a shipping label.
/// </summary>
internal static class ShippingLabelReadiness
{
    public const string Published = "published";

    public static bool IsFailed(ShippingLabel? label) => label?.WorkflowStatus.IsFailed() == true;

    public static bool IsReady(ShippingLabel? label)
    {
        return label?.WorkflowStatus?.State != null &&
               string.Equals(label.WorkflowStatus.State, Published, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWaitDone(ShippingLabel? label) => IsFailed(label) || IsReady(label);
}

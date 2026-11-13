using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Host-free classifier that maps a normalized <see cref="OrderLifecycleState"/> to an
    /// <see cref="OrderActivityClass"/>. It is the single source of truth for "is this order
    /// still active?", so the host adapter, the M3 consumer and the probe all agree.
    /// <para>
    /// The mapping mirrors the canonical RiskPilot.BOT read-only definition of a *working*
    /// order: <c>Working</c>, <c>Accepted</c> and <c>PartFilled</c> are working; <c>Initialized</c>
    /// and <c>Submitted</c> are a pending submit (still live at the host, not yet acknowledged).
    /// A terminal state (<c>Filled</c>, <c>Cancelled</c>, <c>Rejected</c>) is never active, so a
    /// completed or historical order can never be counted as active. Anything else is
    /// <see cref="OrderActivityClass.Unknown"/>, which fails closed.
    /// </para>
    /// </summary>
    public static class OrderStateClassifier
    {
        /// <summary>Classifies a lifecycle state into its activity class.</summary>
        public static OrderActivityClass Classify(OrderLifecycleState state)
        {
            switch (state)
            {
                // Live at the host: acknowledged and resting, or partially filled.
                case OrderLifecycleState.Working:
                case OrderLifecycleState.Accepted:
                case OrderLifecycleState.PartFilled:
                // Live at the host: submitted but not yet acknowledged.
                case OrderLifecycleState.Initialized:
                case OrderLifecycleState.Submitted:
                case OrderLifecycleState.ChangeSubmitted:
                    return OrderActivityClass.Active;

                case OrderLifecycleState.Filled:
                    return OrderActivityClass.Completed;

                case OrderLifecycleState.Cancelled:
                    return OrderActivityClass.Cancelled;

                case OrderLifecycleState.Rejected:
                    return OrderActivityClass.Rejected;

                default:
                    return OrderActivityClass.Unknown;
            }
        }

        /// <summary>True when the state is an active (still live) order state.</summary>
        public static bool IsActive(OrderLifecycleState state)
        {
            return Classify(state) == OrderActivityClass.Active;
        }

        /// <summary>True when the state is a terminal (finished) order state.</summary>
        public static bool IsTerminal(OrderLifecycleState state)
        {
            switch (Classify(state))
            {
                case OrderActivityClass.Completed:
                case OrderActivityClass.Cancelled:
                case OrderActivityClass.Rejected:
                    return true;
                default:
                    return false;
            }
        }
    }
}

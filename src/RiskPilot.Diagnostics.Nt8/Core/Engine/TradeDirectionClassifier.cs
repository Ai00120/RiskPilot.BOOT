using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Deterministic classification of a trade print's aggressor direction from the print price
    /// and the best bid/ask known at classification time.
    /// <para>
    /// NinjaTrader's public Level I surface (<c>MarketDataEventArgs</c>) carries no aggressor side,
    /// so a direction can only ever be *derived* by comparing the print to the quote. The result is
    /// therefore always labelled <see cref="TradeDirectionSource.DerivedFromQuote"/>, never
    /// provider-confirmed, and a print that cannot be classified returns no direction with an
    /// explicit reason instead of a guess.
    /// </para>
    /// This is a pure function so the classification contract is testable without a host.
    /// </summary>
    public static class TradeDirectionClassifier
    {
        /// <summary>
        /// Classifies the direction of a print. <paramref name="direction"/> is null when the
        /// direction could not be determined; <paramref name="reason"/> then carries the exact
        /// cause. When a direction is returned the reason is
        /// <see cref="TradeDirectionUnknownReason.NotApplicable"/>.
        /// </summary>
        public static void Classify(
            double price,
            double? bid,
            double? ask,
            out TradeDirection? direction,
            out TradeDirectionUnknownReason reason)
        {
            if (bid.HasValue && ask.HasValue)
            {
                if (bid.Value > ask.Value)
                {
                    // Crossed book: the BBO cannot be trusted, so do not classify.
                    direction = null;
                    reason = TradeDirectionUnknownReason.BboInvalid;
                    return;
                }

                if (price >= ask.Value)
                {
                    direction = TradeDirection.Buy;
                    reason = TradeDirectionUnknownReason.NotApplicable;
                    return;
                }

                if (price <= bid.Value)
                {
                    direction = TradeDirection.Sell;
                    reason = TradeDirectionUnknownReason.NotApplicable;
                    return;
                }

                // Strictly inside a valid, uncrossed spread.
                direction = null;
                reason = TradeDirectionUnknownReason.PriceInsideSpread;
                return;
            }

            if (ask.HasValue)
            {
                if (price >= ask.Value)
                {
                    direction = TradeDirection.Buy;
                    reason = TradeDirectionUnknownReason.NotApplicable;
                    return;
                }

                direction = null;
                reason = TradeDirectionUnknownReason.BboIncomplete;
                return;
            }

            if (bid.HasValue)
            {
                if (price <= bid.Value)
                {
                    direction = TradeDirection.Sell;
                    reason = TradeDirectionUnknownReason.NotApplicable;
                    return;
                }

                direction = null;
                reason = TradeDirectionUnknownReason.BboIncomplete;
                return;
            }

            direction = null;
            reason = TradeDirectionUnknownReason.NoBboAvailable;
        }

        /// <summary>
        /// Converts a raw host price to a usable positive quote value, or null when the host
        /// reported a sentinel (zero) or a non-finite value. A null means "this side of the BBO
        /// was not populated on this event", which the classifier treats as unavailable.
        /// </summary>
        public static double? ToPositiveOrNull(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
            {
                return null;
            }

            return value;
        }
    }
}

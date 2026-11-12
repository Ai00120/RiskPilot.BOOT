using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class ProbeTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");
        }

        [Fact]
        public void M1_PassesApiSurface_WhenNothingMissing()
        {
            var provider = new FakeDataProvider();
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            var apiResult = Find(results, "M1.API_SURFACE");
            Assert.Equal(DiagnosticStatus.PASS, apiResult.Status);
        }

        [Fact]
        public void M1_FailsApiSurface_WhenRequiredApiMissing()
        {
            var provider = new FakeDataProvider();
            provider.MissingApis.Add("NinjaTrader.Data.MarketDepth");
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            var apiResult = Find(results, "M1.API_SURFACE");
            Assert.Equal(DiagnosticStatus.FAIL, apiResult.Status);
            Assert.Equal(ErrorClass.HostUnavailable, apiResult.ErrorClass);
        }

        [Fact]
        public void M1_HostVersion_IsInconclusiveNeverPass()
        {
            var provider = new FakeDataProvider();
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            var version = Find(results, "M1.HOST_VERSION");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, version.Status);
        }

        [Fact]
        public void M2_NoConnections_IsInconclusive()
        {
            var provider = new FakeDataProvider();
            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.CONNECTIONS").Status);
        }

        [Fact]
        public void M2_ConnectedFeeds_Pass()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ConnectionName = "Rithmic",
                ProviderName = "Rithmic",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });
            provider.Provider = new ProviderRecord { Name = "Rithmic", IsExpectedEvidenceProvider = true };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.CONNECTION.BROKER").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.PROVIDER_IDENTITY").Status);
        }

        [Fact]
        public void M2_UnexpectedProvider_IsInconclusiveNotPass()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "Simulator",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });
            provider.Provider = new ProviderRecord { Name = "Simulator", IsExpectedEvidenceProvider = false };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.PROVIDER_IDENTITY").Status);
        }

        [Fact]
        public void M1_EnvironmentClass_IsInconclusiveWhenUnknown()
        {
            var provider = new FakeDataProvider();
            provider.Environment = new EnvironmentRecord { Environment = EnvironmentClass.Unknown };
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M1.ENVIRONMENT_CLASS").Status);
        }

        [Fact]
        public void Probes_NeverReturnPassWithoutObservation()
        {
            // With an empty provider, no check may be PASS.
            var provider = new FakeDataProvider();
            provider.Environment = new EnvironmentRecord { Environment = EnvironmentClass.Unknown };
            var context = Context(provider);
            foreach (var probe in new IDiagnosticProbe[] { new M1EnvironmentProbe(), new M2ConnectionProbe() })
            {
                foreach (var result in probe.Evaluate(context))
                {
                    if (result.CheckId == "M1.API_SURFACE")
                    {
                        continue; // positively observed from the (fake) host API surface
                    }

                    Assert.NotEqual(DiagnosticStatus.PASS, result.Status);
                }
            }
        }

        private static Core.Result.DiagnosticResult Find(IReadOnlyList<Core.Result.DiagnosticResult> results, string checkId)
        {
            foreach (var result in results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }
    }
}

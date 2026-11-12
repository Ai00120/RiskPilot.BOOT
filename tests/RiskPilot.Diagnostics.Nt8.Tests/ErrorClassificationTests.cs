using System;
using System.IO;
using RiskPilot.Diagnostics.Nt8.Core.Errors;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class ErrorClassificationTests
    {
        [Fact]
        public void NullException_IsNone()
        {
            Assert.Equal(ErrorClass.None, ErrorClassifier.Classify(null));
        }

        [Fact]
        public void NullReference_MapsToHostUnavailable()
        {
            Assert.Equal(ErrorClass.HostUnavailable, ErrorClassifier.Classify(new NullReferenceException()));
        }

        [Fact]
        public void InvalidOperation_MapsToDataUnavailable()
        {
            Assert.Equal(ErrorClass.DataUnavailable, ErrorClassifier.Classify(new InvalidOperationException()));
        }

        [Fact]
        public void Argument_MapsToConfigurationInvalid()
        {
            Assert.Equal(ErrorClass.ConfigurationInvalid, ErrorClassifier.Classify(new ArgumentException()));
            Assert.Equal(ErrorClass.ConfigurationInvalid, ErrorClassifier.Classify(new FormatException()));
        }

        [Fact]
        public void IOException_MapsToSerializationFailure()
        {
            Assert.Equal(ErrorClass.SerializationFailure, ErrorClassifier.Classify(new IOException()));
            Assert.Equal(ErrorClass.SerializationFailure, ErrorClassifier.Classify(new OverflowException()));
        }

        [Fact]
        public void ObjectDisposed_MapsToHostUnavailable()
        {
            Assert.Equal(ErrorClass.HostUnavailable, ErrorClassifier.Classify(new ObjectDisposedException("x")));
        }

        [Fact]
        public void UnknownException_MapsToUnexpected()
        {
            Assert.Equal(ErrorClass.Unexpected, ErrorClassifier.Classify(new Exception("boom")));
        }

        [Fact]
        public void Describe_RedactsSecrets()
        {
            var description = ErrorClassifier.Describe(new Exception("password=hunter2 failed"));
            Assert.DoesNotContain("hunter2", description);
            Assert.Contains("Exception", description);
        }

        [Fact]
        public void ErrorClass_WireNames()
        {
            Assert.Equal("QUEUE_OVERFLOW", ErrorClass.QueueOverflow.ToWire());
            Assert.Equal("SEQUENCE_GAP", ErrorClass.SequenceGap.ToWire());
            Assert.Equal("NONE", ErrorClass.None.ToWire());
        }
    }
}

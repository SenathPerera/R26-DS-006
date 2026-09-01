using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Runtime.Telemetry;
using LaminarVR.AdaptiveMeditation.Telemetry;
using NUnit.Framework;

namespace LaminarVR.AdaptiveMeditation.Tests.EditMode.Telemetry
{
    public sealed class LocalJsonLinesTelemetrySinkTests
    {
        [Test]
        public void PathResolver_UsesSessionScopedJsonLinesFile()
        {
            var path = TelemetryFilePathResolver.ResolveSessionJsonLinesPath(
                "persistent-root",
                "session-123");

            Assert.That(
                path,
                Is.EqualTo(
                    Path.Combine(
                        "persistent-root",
                        "AdaptiveMeditationTelemetry",
                        "session-123.jsonl")));
        }

        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("../session")]
        [TestCase("folder\\session")]
        public void PathResolver_RejectsUnsafeSessionFileNames(string sessionId)
        {
            Assert.Throws<ArgumentException>(
                () => TelemetryFilePathResolver.ResolveSessionJsonLinesPath(
                    "persistent-root",
                    sessionId));
        }

        [Test]
        public async Task Sink_AppendsUtf8LinesFlushesAndContinuesExistingSessionFile()
        {
            var directoryPath = CreateTemporaryDirectoryPath();
            var filePath = Path.Combine(directoryPath, "session.jsonl");
            var configuration = CreateConfiguration(flushEveryEventCount: 2);

            try
            {
                using (var sink = new LocalJsonLinesTelemetrySink(
                    filePath,
                    configuration))
                {
                    await sink.AppendAsync(
                        TelemetryFieldAndEventTests.CreateEvent(sequenceNumber: 1L),
                        CancellationToken.None);
                    await sink.AppendAsync(
                        TelemetryFieldAndEventTests.CreateEvent(sequenceNumber: 2L),
                        CancellationToken.None);

                    Assert.That(
                        ReadAllLinesWhileWriterIsOpen(filePath),
                        Has.Length.EqualTo(2));
                }

                using (var sink = new LocalJsonLinesTelemetrySink(
                    filePath,
                    configuration))
                {
                    await sink.AppendAsync(
                        TelemetryFieldAndEventTests.CreateEvent(
                            sequenceNumber: 3L,
                            critical: true),
                        CancellationToken.None);

                    Assert.That(
                        ReadAllLinesWhileWriterIsOpen(filePath),
                        Has.Length.EqualTo(3));
                }

                var lines = File.ReadAllLines(filePath);
                var bytes = File.ReadAllBytes(filePath);
                Assert.That(lines, Has.Length.EqualTo(3));
                Assert.That(lines[0], Does.Contain("\"sequenceNumber\":1"));
                Assert.That(lines[2], Does.Contain("\"sequenceNumber\":3"));
                Assert.That(
                    bytes.Length < 3
                    || bytes[0] != 0xef
                    || bytes[1] != 0xbb
                    || bytes[2] != 0xbf,
                    Is.True,
                    "JSON Lines file must be UTF-8 without a BOM.");
            }
            finally
            {
                DeleteTemporaryDirectory(directoryPath);
            }
        }

        [Test]
        public void Sink_RejectsEventFromDifferentSchemaOrConfiguration()
        {
            var directoryPath = CreateTemporaryDirectoryPath();
            var filePath = Path.Combine(directoryPath, "session.jsonl");

            try
            {
                using (var sink = new LocalJsonLinesTelemetrySink(
                    filePath,
                    CreateConfiguration()))
                {
                    Assert.ThrowsAsync<ArgumentException>(
                        async () => await sink.AppendAsync(
                            TelemetryFieldAndEventTests.CreateEvent(
                                schemaVersion: "different"),
                            CancellationToken.None));
                }
            }
            finally
            {
                DeleteTemporaryDirectory(directoryPath);
            }
        }

        private static TelemetryLoggingConfiguration CreateConfiguration(
            int flushEveryEventCount = 4)
        {
            return new TelemetryLoggingConfiguration(
                "logging-test",
                2,
                "adaptive-vr-telemetry",
                "0.1-draft",
                flushEveryEventCount);
        }

        private static string CreateTemporaryDirectoryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "LaminarVR.Telemetry.Tests",
                Guid.NewGuid().ToString("N"));
        }

        private static string[] ReadAllLinesWhileWriterIsOpen(string filePath)
        {
            using (var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd()
                    .Replace("\r\n", "\n")
                    .Split(
                        new[] { '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
            }
        }

        private static void DeleteTemporaryDirectory(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Telemetry;

namespace LaminarVR.AdaptiveMeditation.Runtime.Telemetry
{
    public sealed class LocalJsonLinesTelemetrySink : ITelemetryEventSink, IDisposable
    {
        private readonly TelemetryLoggingConfiguration configuration;
        private readonly JsonLinesTelemetryFormatter formatter;
        private readonly SemaphoreSlim writeGate = new SemaphoreSlim(1, 1);
        private readonly StreamWriter writer;
        private int eventsSinceFlush;
        private bool disposed;

        public LocalJsonLinesTelemetrySink(
            string filePath,
            TelemetryLoggingConfiguration configuration,
            JsonLinesTelemetryFormatter formatter = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(
                    "Telemetry file path is required.",
                    nameof(filePath));
            }

            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            this.formatter = formatter ?? new JsonLinesTelemetryFormatter();

            FilePath = Path.GetFullPath(filePath);
            var directoryPath = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                throw new ArgumentException(
                    "Telemetry file must have a parent directory.",
                    nameof(filePath));
            }

            Directory.CreateDirectory(directoryPath);
            var stream = new FileStream(
                FilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                4096,
                false);
        }

        public string FilePath { get; }

        public async Task AppendAsync(
            TelemetryEvent telemetryEvent,
            CancellationToken cancellationToken)
        {
            if (telemetryEvent == null)
            {
                throw new ArgumentNullException(nameof(telemetryEvent));
            }

            ValidateCompatibility(telemetryEvent);
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await writer.WriteLineAsync(formatter.Format(telemetryEvent))
                    .ConfigureAwait(false);
                eventsSinceFlush++;
                if (telemetryEvent.Critical
                    || eventsSinceFlush >= configuration.FlushEveryEventCount)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    eventsSinceFlush = 0;
                }
            }
            finally
            {
                writeGate.Release();
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await writer.FlushAsync().ConfigureAwait(false);
                eventsSinceFlush = 0;
            }
            finally
            {
                writeGate.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            writer.Dispose();
            writeGate.Dispose();
        }

        private void ValidateCompatibility(TelemetryEvent telemetryEvent)
        {
            if (!string.Equals(
                    telemetryEvent.EventSchemaId,
                    configuration.EventSchemaId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    telemetryEvent.EventSchemaVersion,
                    configuration.EventSchemaVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    telemetryEvent.LoggingConfigurationId,
                    configuration.ConfigurationId,
                    StringComparison.Ordinal)
                || telemetryEvent.LoggingConfigurationVersion
                    != configuration.ConfigurationVersion)
            {
                throw new ArgumentException(
                    "Telemetry event and local sink configurations do not match.",
                    nameof(telemetryEvent));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(LocalJsonLinesTelemetrySink));
            }
        }
    }
}

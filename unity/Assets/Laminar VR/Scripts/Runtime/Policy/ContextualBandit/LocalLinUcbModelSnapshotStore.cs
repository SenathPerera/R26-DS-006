using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaminarVR.AdaptiveMeditation.Policy.ContextualBandit;

namespace LaminarVR.AdaptiveMeditation.Runtime.Policy.ContextualBandit
{
    public enum LinUcbSnapshotLoadCode
    {
        Loaded,
        NotFound,
        InvalidJson
    }

    public readonly struct LinUcbSnapshotLoadResult
    {
        public LinUcbSnapshotLoadResult(
            LinUcbSnapshotLoadCode code,
            LinUcbModelSnapshot snapshot,
            string reason)
        {
            Code = code;
            Snapshot = snapshot;
            Reason = reason ?? string.Empty;
        }

        public LinUcbSnapshotLoadCode Code { get; }
        public LinUcbModelSnapshot Snapshot { get; }
        public string Reason { get; }
    }

    public sealed class LocalLinUcbModelSnapshotStore : IDisposable
    {
        private readonly LinUcbModelSnapshotJsonCodec codec;
        private readonly SemaphoreSlim accessGate = new SemaphoreSlim(1, 1);
        private bool disposed;

        public LocalLinUcbModelSnapshotStore(
            string filePath,
            LinUcbModelSnapshotJsonCodec codec = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(
                    "Snapshot file path is required.",
                    nameof(filePath));
            }

            FilePath = Path.GetFullPath(filePath);
            var parent = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                throw new ArgumentException(
                    "Snapshot file must have a parent directory.",
                    nameof(filePath));
            }

            Directory.CreateDirectory(parent);
            this.codec = codec ?? new LinUcbModelSnapshotJsonCodec();
        }

        public string FilePath { get; }

        public async Task SaveAsync(
            LinUcbModelSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var json = codec.Serialize(snapshot);
            await accessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var temporaryPath = FilePath + ".tmp";
                try
                {
                    using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    using (var writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(false),
                        4096,
                        false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await writer.WriteAsync(json).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    ReplaceDestination(temporaryPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
            finally
            {
                accessGate.Release();
            }
        }

        public async Task<LinUcbSnapshotLoadResult> LoadAsync(
            CancellationToken cancellationToken)
        {
            await accessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!File.Exists(FilePath))
                {
                    return new LinUcbSnapshotLoadResult(
                        LinUcbSnapshotLoadCode.NotFound,
                        null,
                        "Snapshot file was not found.");
                }

                string json;
                using (var stream = new FileStream(
                    FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true,
                    4096,
                    false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    json = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!codec.TryDeserialize(json, out var snapshot, out var reason))
                {
                    return new LinUcbSnapshotLoadResult(
                        LinUcbSnapshotLoadCode.InvalidJson,
                        null,
                        reason);
                }

                return new LinUcbSnapshotLoadResult(
                    LinUcbSnapshotLoadCode.Loaded,
                    snapshot,
                    string.Empty);
            }
            finally
            {
                accessGate.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            accessGate.Dispose();
        }

        private void ReplaceDestination(string temporaryPath)
        {
            if (!File.Exists(FilePath))
            {
                File.Move(temporaryPath, FilePath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, FilePath, null);
            }
            catch (PlatformNotSupportedException)
            {
                // Android runtimes may not implement File.Replace. The fully
                // flushed temporary file still prevents partial JSON writes.
                File.Copy(temporaryPath, FilePath, true);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(LocalLinUcbModelSnapshotStore));
            }
        }
    }
}

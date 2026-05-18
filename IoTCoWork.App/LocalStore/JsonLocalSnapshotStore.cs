using System.Text;

namespace IoTCoWork.App.LocalStore;

public sealed class JsonLocalSnapshotStore : ILocalSnapshotStore
{
    private const string SnapshotFileName = "snapshot.json";

    private readonly string _snapshotPath;
    private readonly string _temporarySnapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLocalSnapshotStore()
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IoTCoWork",
            "ImageStudio");

        Directory.CreateDirectory(dataRoot);
        _snapshotPath = Path.Combine(dataRoot, SnapshotFileName);
        _temporarySnapshotPath = _snapshotPath + ".tmp";
    }

    public async ValueTask<string?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_snapshotPath))
            {
                return null;
            }

            return await File.ReadAllTextAsync(_snapshotPath, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(string snapshotJson, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dataRoot = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrEmpty(dataRoot))
            {
                Directory.CreateDirectory(dataRoot);
            }

            await File.WriteAllTextAsync(_temporarySnapshotPath, snapshotJson, Encoding.UTF8, cancellationToken);
            File.Move(_temporarySnapshotPath, _snapshotPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existed = File.Exists(_snapshotPath);
            if (existed)
            {
                File.Delete(_snapshotPath);
            }

            if (File.Exists(_temporarySnapshotPath))
            {
                File.Delete(_temporarySnapshotPath);
            }

            return existed;
        }
        finally
        {
            _gate.Release();
        }
    }
}

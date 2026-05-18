namespace IoTCoWork.App.LocalStore;

public interface ILocalSnapshotStore
{
    ValueTask<string?> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(string snapshotJson, CancellationToken cancellationToken);
    ValueTask<bool> DeleteAsync(CancellationToken cancellationToken);
}

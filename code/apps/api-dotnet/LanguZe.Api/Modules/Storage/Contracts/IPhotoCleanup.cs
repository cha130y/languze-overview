namespace LanguZe.Api.Modules.Storage.Contracts;

/// <summary>The photo cleanup task, as the writers of cleanup records see it.</summary>
public interface IPhotoCleanup
{
    /// <summary>Called once a transaction that wrote a cleanup record has committed, so the next run sweeps.</summary>
    void Expect();
}

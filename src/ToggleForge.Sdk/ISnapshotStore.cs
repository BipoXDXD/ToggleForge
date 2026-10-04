using System.Threading;
using System.Threading.Tasks;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// Onde o snapshot sobrevive a um reinício do jogo. O .NET usa <see cref="FileSnapshotStore"/>; a Unity terá
    /// um adaptador em <c>Application.persistentDataPath</c> (ADR 0010).
    /// </summary>
    /// <remarks>
    /// Uma falha de disco pode lançar exceção: o SDK a absorve, e o jogo segue só com o que está em memória.
    /// </remarks>
    public interface ISnapshotStore
    {
        /// <returns>Nulo quando não há nada guardado ou o que está guardado não se lê.</returns>
        Task<StoredSnapshot?> LoadAsync(CancellationToken cancellationToken);

        /// <summary>Substitui o que está guardado; quem lê nunca vê uma gravação pela metade.</summary>
        Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken);
    }
}

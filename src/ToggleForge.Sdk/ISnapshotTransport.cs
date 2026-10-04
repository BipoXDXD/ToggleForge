using System.Threading;
using System.Threading.Tasks;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// Como o SDK chega a <c>GET /sdk/v1/snapshot</c>. O .NET usa <see cref="HttpSnapshotTransport"/>; a Unity
    /// terá um adaptador próprio (ADR 0010).
    /// </summary>
    public interface ISnapshotTransport
    {
        /// <param name="eTag">A ETag do snapshot atual, para o servidor responder que nada mudou; nula sem snapshot.</param>
        /// <remarks>
        /// Falha de rede, do servidor ou timeout volta como <see cref="SnapshotResponse.Failed"/>, nunca como
        /// exceção. Só o cancelamento pedido por quem chamou propaga. Cada tentativa tem prazo, que cobre também a
        /// leitura do corpo.
        /// </remarks>
        Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken);
    }
}

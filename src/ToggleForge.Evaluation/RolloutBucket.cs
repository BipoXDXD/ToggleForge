using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Algoritmo de rollout v1 (docs/rollout-algorithm.md): SHA-256 da codificação canônica dos campos,
    /// truncado em 64 bits e reduzido a um bucket de 0 a 9999. É contrato público: toda implementação
    /// (SDKs, simulador) deve produzir os mesmos buckets dos vetores em tests/.../Vectors/rollout-v1.json.
    /// </summary>
    public static class RolloutBucket
    {
        public const string AlgorithmTag = "toggleforge.rollout.v1";

        public const int BucketCount = RolloutPercentage.MaxBasisPoints;

        private const int LengthPrefixSize = 4;

        private const int HashPrefixSize = 8;

        public static int Compute(
            ProjectKey projectKey, EnvironmentKey environmentKey, FlagKey flagKey, string seed, PlayerId playerId)
        {
            if (seed is null)
            {
                throw new ArgumentNullException(nameof(seed));
            }

            var input = EncodeCanonically(
                AlgorithmTag, projectKey.Value, environmentKey.Value, flagKey.Value, seed, playerId.Value);

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(input);
            var hashPrefix = BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, HashPrefixSize));
            return (int)(hashPrefix % BucketCount);
        }

        /// <summary>Prefixa cada campo com o tamanho, para que ("ab", "c") e ("a", "bc") não colidam.</summary>
        private static byte[] EncodeCanonically(params string[] fields)
        {
            using var stream = new MemoryStream();
            Span<byte> lengthPrefix = stackalloc byte[LengthPrefixSize];
            foreach (var field in fields)
            {
                var bytes = Encoding.UTF8.GetBytes(field);
                BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
                stream.Write(lengthPrefix);
                stream.Write(bytes, 0, bytes.Length);
            }

            return stream.ToArray();
        }
    }
}

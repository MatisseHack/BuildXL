// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Information combined with a weak fingerprint to yield a strong fingerprint.
    /// </summary>
    public readonly struct Selector : IEquatable<Selector>
    {
        private static readonly ByteArrayComparer ByteComparer = new ByteArrayComparer();

        /// <summary>
        ///     Initializes a new instance of the <see cref="Selector" /> struct.
        /// </summary>
        public Selector(ContentHash contentHash, byte[] output = null)
        {
            ContentHash = contentHash;
            Output = output;
        }

        /// <summary>
        ///     Gets build Engine Input Content Hash.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets build Engine Selector Output, limited to 1kB.
        /// </summary>
        public byte[] Output { get; }

        /// <inheritdoc />
        public bool Equals(Selector other)
        {
            return ContentHash.Equals(other.ContentHash) && ByteArrayComparer.ArraysEqual(Output, other.Output);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ContentHash.GetHashCode() ^ ByteComparer.GetHashCode(Output);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var hex = Output != null ? HexUtilities.BytesToHex(Output) : string.Empty;
            return $"ContentHash=[{ContentHash}], Output=[{hex}]";
        }

        /// <nodoc />
        public static bool operator ==(Selector left, Selector right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Selector left, Selector right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static Selector Random(HashType hashType = HashType.Vso0, int outputLength = 2)
        {
            byte[] output = outputLength == 0 ? null : ThreadSafeRandom.GetBytes(outputLength);
            return new Selector(ContentHash.Random(hashType), output);
        }
    }
}

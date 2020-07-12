using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace disParityLib.parity {
	class HashContainer {
		public HashContainer() { }
		public HashContainer(List<HashValue> hashValues) {
			this.HashValues = hashValues;
		}
		public IList<HashValue> HashValues { get; } = new List<HashValue>();
	}
	class HashValue {
		private HashAlgorithm algorithm;
		private ImmutableArray<byte> hashCode;
		public HashValue(HashAlgorithm algorithm) : this(algorithm, null) { }
		public HashValue(HashAlgorithm algorithm, byte[] hashCode) {
			this.algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
			hashCode = hashCode ?? new byte[algorithm.HashSize];
			if (hashCode.Length != algorithm.HashSize) {
				throw new ArgumentException(nameof(hashCode));
			}
			this.hashCode = ImmutableArray.Create(hashCode);
		}

		public HashAlgorithm Algorithm { get { return algorithm; } }
		public byte[] HashCode { get { return hashCode.ToArray(); } }
	}
}

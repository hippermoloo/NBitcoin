﻿using NBitcoin.BouncyCastle.Crypto.Digests;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using NBitcoin.RPC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin.Logging;

namespace NBitcoin.Altcoins.Elements
{
	public class ConfidentialValue : ConfidentialCommitment
	{
		static Def _Def = new Def()
		{
			ExplicitSize = 9,
			PrefixA = 8,
			PrefixB = 9
		};
		protected override Def GetDef()
		{
			return _Def;
		}

		public ConfidentialValue()
		{

		}
		public ConfidentialValue(Money amount) : base(ToCommitment(amount))
		{

		}

		public bool IsNull()
		{
			return this.Commitment == null || this.Commitment.Length == 0;
		}

		private static byte[] ToCommitment(Money amount)
		{
			if(amount == null)
				return null;
			var ms = new MemoryStream();
			var stream = new BitcoinStream(ms, true);
			ms.WriteByte(1);
			stream.BigEndianScope();
			long m = amount.Satoshi;
			stream.ReadWrite(ref m);
			return ms.ToArrayEfficient();
		}

		public Money Amount
		{
			get
			{
				if(!IsExplicit)
					return null;
				var stream = new BitcoinStream(Commitment);
				stream.BigEndianScope();
				stream.Inner.ReadByte();
				long m = 0;
				stream.ReadWrite(ref m);
				return Money.Satoshis(m);
			}
		}
	}

	public abstract class ConfidentialCommitment : IBitcoinSerializable
	{
		protected class Def
		{
			public byte ExplicitSize;
			public byte PrefixA;
			public byte PrefixB;
		}

		public ConfidentialCommitment()
		{

		}
		public ConfidentialCommitment(byte[] commitment)
		{
			_Commitment = commitment ?? _Empty;
		}

		protected abstract Def GetDef();

		static byte[] _Empty = new byte[0];
		byte[] _Commitment = _Empty;
		public byte[] Commitment
		{
			get
			{
				return _Commitment;
			}
			private set
			{
				_Commitment = value;
			}
		}

		const int nCommittedSize = 33;
		public void ReadWrite(BitcoinStream stream)
		{
			byte version = _Commitment.Length == 0 ? (byte)0 : _Commitment[0];
			stream.ReadWrite(ref version);
			if(!stream.Serializing)
			{
				var def = GetDef();
				switch(version)
				{
					/* Null */
					case 0:
						_Commitment = _Empty;
						return;
					/* Explicit value */
					case 1:
					/* Trust-me! asset generation */
					case 0xff:
						_Commitment = new byte[def.ExplicitSize];
						break;

					default:
						/* Confidential commitment */
						if(version == def.PrefixA || version == def.PrefixB)
						{
							_Commitment = new byte[nCommittedSize];
							break;
						}
						else
						{
							/* Invalid serialization! */
							throw new FormatException("Unrecognized serialization prefix");
						}
				}
				_Commitment[0] = version;
			}

			if(_Commitment.Length > 1)
			{
				stream.ReadWrite(ref _Commitment, 1, _Commitment.Length - 1);
			}
		}

		public bool IsExplicit
		{
			get
			{
				return _Commitment.Length == GetDef().ExplicitSize && _Commitment[0] == 1;
			}
		}
	}

	public class AssetIssuance : IBitcoinSerializable
	{
		public AssetIssuance()
		{
		}

		public bool IsNull()
		{
			return (_Amount == null || _Amount.IsNull()) && (InflationKeys == null || InflationKeys.IsNull());
		}

		uint256 _BlindingNonce = uint256.Zero;
		public uint256 BlindingNonce
		{
			get
			{
				return _BlindingNonce;
			}
			set
			{
				_BlindingNonce = value;
			}
		}


		uint256 _Entropy = uint256.Zero;
		public uint256 Entropy
		{
			get
			{
				return _Entropy;
			}
			set
			{
				_Entropy = value;
			}
		}


		ConfidentialValue _Amount = new ConfidentialValue(0);
		public ConfidentialValue ConfidentialAmount
		{
			get
			{
				return _Amount;
			}
			set
			{
				_Amount = value;
			}
		}


		ConfidentialValue _InflationKeys = new ConfidentialValue(0);
		public ConfidentialValue InflationKeys
		{
			get
			{
				return _InflationKeys;
			}
			set
			{
				_InflationKeys = value;
			}
		}

		public Money Amount
		{
			get
			{
				return _Amount.Amount;
			}
		}

		public void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref _BlindingNonce);
			stream.ReadWrite(ref _Entropy);
			stream.ReadWrite(ref _Amount);
			stream.ReadWrite(ref _InflationKeys);
		}
	}


	public class ElementsTxIn : TxIn
	{
		public ElementsTxIn(ConsensusFactory consensusFactory)
		{
			if (consensusFactory == null)
				throw new ArgumentNullException(nameof(consensusFactory));
			ElementsConsensusFactory = consensusFactory;
		}
		public uint256 GetIssuedAssetId()
		{
			if(AssetIssuance?.BlindingNonce != uint256.Zero)
				return null;
			var assetEntropy = new MerkleNode(
				new MerkleNode(Hashes.Hash256(PrevOut.ToBytes())),
				new MerkleNode(new uint256(AssetIssuance.Entropy.ToBytes())));
			UpdateFastHash(assetEntropy);

			var asset = new MerkleNode(
				new MerkleNode(assetEntropy.Hash),
				new MerkleNode(uint256.Zero));
			UpdateFastHash(asset);
			return asset.Hash;
		}
		public void UpdateFastHash(MerkleNode node)
		{
			var right = node.Right ?? node.Left;
			if (node.Left != null && node.Left.Hash != null && right.Hash != null)
				node.Hash = FastSHA256(node.Left.Hash.ToBytes().Concat(right.Hash.ToBytes()).ToArray());
		}

		private uint256 FastSHA256(byte[] data, int offset, int count)
		{
			Sha256Digest sha256 = new Sha256Digest();
			sha256.BlockUpdate(data, offset, count);
			return new uint256(sha256.MidState);
		}
		private uint256 FastSHA256(byte[] data)
		{
			return FastSHA256(data, 0, data.Length);
		}

		internal const uint OUTPOINT_INDEX_MASK = 0x7fffffff;
		internal const uint OUTPOINT_ISSUANCE_FLAG = (1U << 31);


		public override void ReadWrite(BitcoinStream stream)
		{
			bool fHasAssetIssuance = false;
			OutPoint outpoint = null;

			if(stream.Serializing)
			{
				if(PrevOut.IsNull)
				{
					fHasAssetIssuance = false;
					outpoint = prevout.Clone();
				}
				else
				{
					if((prevout.N & ~OUTPOINT_INDEX_MASK) != 0)
						throw new FormatException("Prevout.N should not have OUTPOINT_INDEX_MASK");
					fHasAssetIssuance = _AssetIssuance != null && !_AssetIssuance.IsNull();
					outpoint = prevout.Clone();
					outpoint.N = prevout.N & OUTPOINT_INDEX_MASK;
					if(fHasAssetIssuance)
					{
						outpoint.N |= OUTPOINT_ISSUANCE_FLAG;
					}
				}
			}

			stream.ReadWrite(ref outpoint);

			if(!stream.Serializing)
			{
				if(outpoint.IsNull)
				{
					fHasAssetIssuance = false;
					prevout = outpoint;
				}
				else
				{
					fHasAssetIssuance = (outpoint.N & OUTPOINT_ISSUANCE_FLAG) != 0;
					prevout.Hash = outpoint.Hash;
					prevout.N = outpoint.N & OUTPOINT_INDEX_MASK;
				}
			}

			stream.ReadWrite(ref scriptSig);
			stream.ReadWrite(ref nSequence);

			if(fHasAssetIssuance)
			{
				stream.ReadWrite(ref _AssetIssuance);
			}
			else if(!stream.Serializing)
				_AssetIssuance = null;
		}

		AssetIssuance _AssetIssuance = new AssetIssuance();
		public AssetIssuance AssetIssuance
		{
			get
			{
				return _AssetIssuance;
			}
			set
			{
				_AssetIssuance = value;
			}
		}

		public byte[] IssuanceAmountRangeProof
		{
			get;
			set;
		}

		public byte[] InflationKeysRangeProof
		{
			get; set;
		}

		public WitScript PeginWitScript { get; set; }

		public ConsensusFactory ElementsConsensusFactory { get; set; }
		public override ConsensusFactory GetConsensusFactory()
		{
			return ElementsConsensusFactory;
		}

		public override TxIn Clone()
		{
			var txIn = (ElementsTxIn)base.Clone();
			txIn.InflationKeysRangeProof = InflationKeysRangeProof;
			txIn.IssuanceAmountRangeProof = IssuanceAmountRangeProof;
			txIn.AssetIssuance = AssetIssuance;
			return txIn;
		}
	}

	public class ConfidentialNonce : ConfidentialCommitment
	{
		static Def _Def = new Def() { ExplicitSize = 33, PrefixA = 2, PrefixB = 3 };
		protected override Def GetDef()
		{
			return _Def;
		}
	}

	public class ConfidentialAsset : ConfidentialCommitment
	{
		static Def _Def = new Def() { ExplicitSize = 33, PrefixA = 10, PrefixB = 11 };
		protected override Def GetDef()
		{
			return _Def;
		}

		static uint256 _BTC = new uint256("09f663de96be771f50cab5ded00256ffe63773e2eaa9a604092951cc3d7c6621");
		public ConfidentialAsset() : base(ToCommitment(_BTC))
		{

		}

		public bool? IsBTC
		{
			get
			{
				if(!IsExplicit)
					return null;
				return AssetId == _BTC;
			}
		}
		public ConfidentialAsset(uint256 id) : base(ToCommitment(id))
		{

		}

		private static byte[] ToCommitment(uint256 id)
		{
			if(id == null)
				return null;
			var ms = new MemoryStream();
			var stream = new BitcoinStream(ms, true);
			ms.WriteByte(1);
			stream.ReadWrite(ref id);
			return ms.ToArrayEfficient();
		}

		public uint256 AssetId
		{
			get
			{
				if(!IsExplicit)
					return null;
				var stream = new BitcoinStream(Commitment);
				stream.Inner.ReadByte();
				uint256 m = 0;
				stream.ReadWrite(ref m);
				return m;
			}
		}
	}

	public class ElementsTxOut : TxOut
	{
		public ElementsTxOut()
		{

		}

		public bool IsFee
		{
			get
			{
				return this.ScriptPubKey == Script.Empty && _ConfidentialValue.IsExplicit && _Asset.IsExplicit;
			}
		}

		ConfidentialValue _ConfidentialValue = new ConfidentialValue();
		public ConfidentialValue ConfidentialValue
		{
			get
			{
				return _ConfidentialValue;
			}
			set
			{
				_ConfidentialValue = value;
			}
		}


		ConfidentialNonce _Nonce = new ConfidentialNonce();
		public ConfidentialNonce Nonce
		{
			get
			{
				return _Nonce;
			}
			set
			{
				_Nonce = value;
			}
		}


		ConfidentialAsset _Asset = new ConfidentialAsset();
		public ConfidentialAsset Asset
		{
			get
			{
				return _Asset;
			}
			set
			{
				_Asset = value;
			}
		}

		public byte[] SurjectionProof
		{
			get;
			set;
		}
		public byte[] RangeProof
		{
			get;
			set;
		}

		public override void ReadWrite(BitcoinStream stream)
		{
			stream.ReadWrite(ref _Asset);
			stream.ReadWrite(ref _ConfidentialValue);
			if(!stream.Serializing)
				UpdateValue();
			stream.ReadWrite(ref _Nonce);
			stream.ReadWrite(ref publicKey);
		}

		/// <summary>
		/// Make sure TxOut.Value reflect ConfidentialValue
		/// </summary>
		public void UpdateValue()
		{
			Value = _ConfidentialValue.Amount;
		}

		/// <summary>
		/// Make sure ConfidentialValue reflects Value
		/// </summary>
		public void UpdateConfidentialValue()
		{
			if(Value != null)
				_ConfidentialValue = new ConfidentialValue(Value);
		}

		public override TxOut Clone()
		{
			var txOut = (ElementsTxOut)base.Clone();
			txOut.SurjectionProof = SurjectionProof;
			txOut.RangeProof = RangeProof;
			return txOut;
		}
	}
#pragma warning disable CS0618 // Type or member is obsolete
	public class ElementsTransaction : Transaction
	{
		public ConsensusFactory ElementsConsensusFactory { get; set; }
		public override ConsensusFactory GetConsensusFactory()
		{
			return ElementsConsensusFactory;
		}

		public Money Fee
		{
			get
			{
				return (Money)Outputs.Cast<ElementsTxOut>().Where(o => o.IsFee).Select(o => o.Value).Sum(Money.Zero);
			}
		}

		public override Money GetFee(ICoin[] spentCoins)
		{
			return Fee;
		}

		public override bool HasWitness
		{
			get
			{
				return Inputs.Cast<ElementsTxIn>().Any(i => 
				(i.WitScript != WitScript.Empty && i.WitScript != null) ||
				(i.InflationKeysRangeProof != null && i.InflationKeysRangeProof.Length != 0) ||
				(i.IssuanceAmountRangeProof != null && i.IssuanceAmountRangeProof.Length != 0) ||
				(i.PeginWitScript != WitScript.Empty && i.PeginWitScript != null))

				||
				
				Outputs.Cast<ElementsTxOut>().Any(i =>
					(i.RangeProof != null && i.RangeProof.Length != 0) ||
					(i.SurjectionProof != null && i.SurjectionProof.Length != 0));
			}
		}

		public override void ReadWrite(BitcoinStream stream)
		{
			var witSupported = (((uint)stream.TransactionOptions & (uint)TransactionOptions.Witness) != 0) &&
								stream.ProtocolCapabilities.SupportWitness;

			byte flags = (byte)(this.HasWitness && witSupported ? 1 : 0);
			stream.ReadWrite(ref nVersion);
			stream.ReadWrite(ref flags);
			stream.ReadWrite<TxInList, TxIn>(ref vin);
			vin.Transaction = this;
			stream.ReadWrite<TxOutList, TxOut>(ref vout);
			vout.Transaction = this;
			stream.ReadWrite(ref nLockTime);
			if ((flags & 1) != 0)
			{
				flags ^= 1;
				var wit = new ElementsWitness(Inputs, Outputs);
				wit.ReadWrite(stream);
			}

			if(flags != 0)
				throw new FormatException("Unknown transaction optional data");
		}

		class ElementsWitness
		{
			TxInList _Inputs;
			TxOutList _Outputs;
			public ElementsWitness(TxInList inputs, TxOutList outputs)
			{
				_Inputs = inputs;
				_Outputs = outputs;
			}

			internal bool IsNull()
			{
				return _Inputs.Cast<ElementsTxIn>().All(i => i.WitScript.PushCount == 0 && i.IssuanceAmountRangeProof == null && i.InflationKeysRangeProof == null)
				&& _Outputs.Cast<ElementsTxOut>().All(i => i.SurjectionProof == null && i.RangeProof == null);
			}

			internal void ReadWrite(BitcoinStream stream)
			{
				for (int i = 0; i < _Inputs.Count; i++)
				{
					if (stream.Serializing)
					{
						var bytes = ((ElementsTxIn)_Inputs[i]).IssuanceAmountRangeProof;
						stream.ReadWriteAsVarString(ref bytes);


						bytes = ((ElementsTxIn)_Inputs[i]).InflationKeysRangeProof;
						stream.ReadWriteAsVarString(ref bytes);

						bytes = (_Inputs[i].WitScript ?? WitScript.Empty).ToBytes();
						stream.ReadWrite(ref bytes);

						bytes = (((ElementsTxIn)_Inputs[i]).PeginWitScript ?? WitScript.Empty).ToBytes();
						stream.ReadWrite(ref bytes);
					}
					else
					{
						byte[] bytes = null;
						stream.ReadWriteAsVarString(ref bytes);
						((ElementsTxIn)_Inputs[i]).IssuanceAmountRangeProof = bytes;

						bytes = null;
						stream.ReadWriteAsVarString(ref bytes);
						((ElementsTxIn)_Inputs[i]).InflationKeysRangeProof = bytes;

						((ElementsTxIn)_Inputs[i]).WitScript = WitScript.Load(stream);

						((ElementsTxIn)_Inputs[i]).PeginWitScript = WitScript.Load(stream);
					}
				}

				for (int i = 0; i < _Outputs.Count; i++)
				{
					if (stream.Serializing)
					{
						var bytes = ((ElementsTxOut)_Outputs[i]).SurjectionProof;
						stream.ReadWriteAsVarString(ref bytes);

						bytes = ((ElementsTxOut)_Outputs[i]).RangeProof;
						stream.ReadWriteAsVarString(ref bytes);

					}
					else
					{
						byte[] bytes = null;
						stream.ReadWriteAsVarString(ref bytes);
						((ElementsTxOut)_Outputs[i]).SurjectionProof = bytes;

						bytes = null;
						stream.ReadWriteAsVarString(ref bytes);
						((ElementsTxOut)_Outputs[i]).RangeProof = bytes;
					}
				}

				if (IsNull())
					throw new FormatException("Superfluous witness record");
			}
		}

		public override uint256 GetSignatureHash(Script scriptCode, int nIn, SigHash nHashType, Money amount, HashVersion sigversion, PrecomputedTransactionData precomputedTransactionData)
		{
			if (sigversion == HashVersion.Witness)
			{
				if (amount == null)
					throw new ArgumentException("The amount of the output being signed must be provided", "amount");
				uint256 hashPrevouts = uint256.Zero;
				uint256 hashSequence = uint256.Zero;
				uint256 hashOutputs = uint256.Zero;
				uint256 hashIssuance = uint256.Zero;

				if ((nHashType & SigHash.AnyoneCanPay) == 0)
				{
					hashPrevouts = precomputedTransactionData == null ?
								   GetHashPrevouts() : precomputedTransactionData.HashPrevouts;
				}

				if ((nHashType & SigHash.AnyoneCanPay) == 0 && ((uint)nHashType & 0x1f) != (uint)SigHash.Single && ((uint)nHashType & 0x1f) != (uint)SigHash.None)
				{
					hashSequence = precomputedTransactionData == null ?
								   GetHashSequence() : precomputedTransactionData.HashSequence;
				}

				if ((nHashType & SigHash.AnyoneCanPay) == 0)
				{
					hashIssuance = GetIssuanceHash();
				}

				if (((uint)nHashType & 0x1f) != (uint)SigHash.Single && ((uint)nHashType & 0x1f) != (uint)SigHash.None)
				{
					hashOutputs = precomputedTransactionData == null ?
									GetHashOutputs() : precomputedTransactionData.HashOutputs;
				}
				else if (((uint)nHashType & 0x1f) == (uint)SigHash.Single && nIn < this.Outputs.Count)
				{
					BitcoinStream ss = CreateHashWriter(sigversion);
					ss.ReadWrite(this.Outputs[nIn]);
					hashOutputs = GetHash(ss);
				}

				BitcoinStream sss = CreateHashWriter(sigversion);
				// Version
				sss.ReadWrite(this.Version);
				// Input prevouts/nSequence (none/all, depending on flags)
				sss.ReadWrite(hashPrevouts);
				sss.ReadWrite(hashSequence);
				sss.ReadWrite(hashIssuance);
				// The input being signed (replacing the scriptSig with scriptCode + amount)
				// The prevout may already be contained in hashPrevout, and the nSequence
				// may already be contain in hashSequence.
				sss.ReadWrite(Inputs[nIn].PrevOut);
				sss.ReadWrite(scriptCode);
				sss.ReadWrite(amount.Satoshi);
				sss.ReadWrite((uint)Inputs[nIn].Sequence);
				var assetIssuance = (this.Inputs[nIn] as ElementsTxIn).AssetIssuance;
				if (assetIssuance != null && !assetIssuance.IsNull())
					sss.ReadWrite(assetIssuance);
				// Outputs (none/one/all, depending on flags)
				sss.ReadWrite(hashOutputs);
				// Locktime
				sss.ReadWriteStruct(LockTime);
				// Sighash type
				sss.ReadWrite((uint)nHashType);

				return GetHash(sss);
			}
			

			if (nIn >= Inputs.Count)
			{
				Logs.Utils.LogWarning("ERROR: SignatureHash() : nIn=" + nIn + " out of range");
				return uint256.One;
			}

			var hashType = nHashType & (SigHash)31;

			// Check for invalid use of SIGHASH_SINGLE
			if (hashType == SigHash.Single)
			{
				if (nIn >= Outputs.Count)
				{
					Logs.Utils.LogWarning("ERROR: SignatureHash() : nOut=" + nIn + " out of range");
					return uint256.One;
				}
			}

			var scriptCopy = new Script(scriptCode._Script);
			scriptCopy = scriptCopy.FindAndDelete(OpcodeType.OP_CODESEPARATOR);

			var txCopy = GetConsensusFactory().CreateTransaction();
			txCopy.FromBytes(this.ToBytes());
			//Set all TxIn script to empty string
			foreach (var txin in txCopy.Inputs)
			{
				txin.ScriptSig = new Script();
			}
			//Copy subscript into the txin script you are checking
			txCopy.Inputs[nIn].ScriptSig = scriptCopy;

			if (hashType == SigHash.None)
			{
				//The output of txCopy is set to a vector of zero size.
				txCopy.Outputs.Clear();

				//All other inputs aside from the current input in txCopy have their nSequence index set to zero
				foreach (var input in txCopy.Inputs.Where((x, i) => i != nIn))
					input.Sequence = 0;
			}
			else if (hashType == SigHash.Single)
			{
				//The output of txCopy is resized to the size of the current input index+1.
				txCopy.Outputs.RemoveRange(nIn + 1, txCopy.Outputs.Count - (nIn + 1));
				//All other txCopy outputs aside from the output that is the same as the current input index are set to a blank script and a value of (long) -1.
				for (var i = 0; i < txCopy.Outputs.Count; i++)
				{
					if (i == nIn)
						continue;
					txCopy.Outputs[i] = txCopy.Outputs.CreateNewTxOut();
				}
				//All other txCopy inputs aside from the current input are set to have an nSequence index of zero.
				foreach (var input in txCopy.Inputs.Where((x, i) => i != nIn))
					input.Sequence = 0;
			}


			if ((nHashType & SigHash.AnyoneCanPay) != 0)
			{
				//The txCopy input vector is resized to a length of one.
				var script = txCopy.Inputs[nIn];
				txCopy.Inputs.Clear();
				txCopy.Inputs.Add(script);
				//The subScript (lead in by its length as a var-integer encoded!) is set as the first and only member of this vector.
				txCopy.Inputs[0].ScriptSig = scriptCopy;
			}


			//Serialize TxCopy, append 4 byte hashtypecode
			var stream = CreateHashWriter(sigversion);
			txCopy.ReadWrite(stream);
			stream.ReadWrite((uint)nHashType);
			return GetHash(stream);
		}

		private uint256 GetIssuanceHash()
		{
			uint256 hashOutputs;
			BitcoinStream ss = CreateHashWriter(HashVersion.Witness);
			for (int i = 0; i < this.Inputs.Count; i++)
			{
				var assetIssuance = (this.Inputs[i] as ElementsTxIn)?.AssetIssuance;
				if (assetIssuance == null || assetIssuance.IsNull())
					ss.ReadWrite((byte)1);
				else
					ss.ReadWrite(ref assetIssuance);
			}
			hashOutputs = GetHash(ss);
			return hashOutputs;
		}
		private static uint256 GetHash(BitcoinStream stream)
		{
			var preimage = ((HashStreamBase)stream.Inner).GetHash();
			stream.Inner.Dispose();
			return preimage;
		}
	}
#pragma warning restore CS0618 // Type or member is obsolete
}

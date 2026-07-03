using System;
using System.Collections.Generic;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace ConnectOnion.Protocol;

/// <summary>
/// Ed25519 agent identity. Port of the Node path in <c>address.ts</c> plus
/// <c>signPayload</c> in <c>auth.ts</c>.
///
/// Key format matches the SDK: the address is <c>"0x" + hex(publicKey)</c> and
/// the private key is a raw 32-byte seed. Signatures are lowercase hex of the
/// 64-byte Ed25519 signature over the canonical-JSON payload — identical to
/// Node's <c>crypto.sign(null, msg, key)</c> and browser <c>nacl.sign.detached</c>.
/// </summary>
public sealed class AgentIdentity
{
    public string Address { get; }
    public byte[] PublicKey { get; }
    public byte[] PrivateSeed { get; }

    public string ShortAddress =>
        Address.Length >= 10 ? $"{Address[..6]}...{Address[^4..]}" : Address;

    private AgentIdentity(string address, byte[] publicKey, byte[] privateSeed)
    {
        Address = address;
        PublicKey = publicKey;
        PrivateSeed = privateSeed;
    }

    public static AgentIdentity Generate()
    {
        var random = new SecureRandom();
        var seed = new byte[32];
        random.NextBytes(seed);
        return FromSeed(seed);
    }

    /// <summary>Reconstructs an identity from its stored 32-byte private seed.</summary>
    public static AgentIdentity FromSeed(byte[] seed)
    {
        if (seed.Length != 32)
            throw new ArgumentException("Ed25519 seed must be 32 bytes.", nameof(seed));

        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var address = "0x" + Hex.ToLowerString(publicKey);
        return new AgentIdentity(address, publicKey, (byte[])seed.Clone());
    }

    /// <summary>Signs a message and returns the lowercase-hex 64-byte signature.</summary>
    public string Sign(string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var privateKey = new Ed25519PrivateKeyParameters(PrivateSeed, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
        var signature = signer.GenerateSignature();
        return Hex.ToLowerString(signature);
    }

    /// <summary>
    /// Verifies a hex signature against a 0x-address. Port of <c>verify()</c>.
    /// </summary>
    public static bool Verify(string address, string message, string signatureHex)
    {
        try
        {
            if (!address.StartsWith("0x", StringComparison.Ordinal) || address.Length != 66)
                return false;

            var publicKeyBytes = Convert.FromHexString(address[2..]);
            var messageBytes = Encoding.UTF8.GetBytes(message);
            var signature = Convert.FromHexString(signatureHex);

            var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, publicKey);
            verifier.BlockUpdate(messageBytes, 0, messageBytes.Length);
            return verifier.VerifySignature(signature);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Signs a payload and returns the envelope the SDK sends:
    /// <c>{ payload, from, signature, timestamp }</c>. Port of
    /// <c>signPayload</c> in <c>auth.ts</c>. The payload is serialized with
    /// <see cref="CanonicalJson"/> before signing.
    /// </summary>
    public SignedEnvelope SignPayload(IReadOnlyList<KeyValuePair<string, object?>> payload)
    {
        var canonical = CanonicalJson.Serialize(payload);
        var signature = Sign(canonical);

        object? timestamp = null;
        foreach (var pair in payload)
        {
            if (pair.Key == "timestamp") { timestamp = pair.Value; break; }
        }

        var payloadMap = new Dictionary<string, object?>(payload.Count);
        foreach (var pair in payload) payloadMap[pair.Key] = pair.Value;

        return new SignedEnvelope
        {
            Payload = payloadMap,
            From = Address,
            Signature = signature,
            Timestamp = timestamp,
        };
    }
}

/// <summary>The signed request envelope spread into CONNECT/ONBOARD messages.</summary>
public sealed class SignedEnvelope
{
    public Dictionary<string, object?> Payload { get; init; } = new();
    public string From { get; init; } = "";
    public string Signature { get; init; } = "";
    public object? Timestamp { get; init; }
}

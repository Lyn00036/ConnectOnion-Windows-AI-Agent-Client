using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ConnectOnion.Protocol;

// JS <-> C# signing conformance gate (Step 9 prerequisite).
//
// Signs an identical payload with the same seed in the Node reference
// (mirroring address.ts) and in the C# port, then asserts the address,
// canonical JSON, and Ed25519 signature all match byte-for-byte. If this
// fails, every signed CONNECT/ONBOARD would be silently rejected by the agent,
// so this must pass before any transport code is trusted.

const string seedHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

var seed = Convert.FromHexString(seedHex);
var identity = AgentIdentity.FromSeed(seed);

// Same payload as ref-sign.js (order irrelevant — both sides sort keys).
var payload = new List<KeyValuePair<string, object?>>
{
    new("to", identity.Address),
    new("timestamp", 1700000000L),
    new("invite_code", "abc+DEF/123"),
    new("payment", 5L),
};

var canonical = CanonicalJson.Serialize(payload);
var signature = identity.Sign(canonical);

var reference = RunNodeReference(seedHex);

Console.WriteLine("field       | C#" );
Console.WriteLine("------------+--------------------------------------------------");
Console.WriteLine($"address     | {identity.Address}");
Console.WriteLine($"  node      | {reference.Address}");
Console.WriteLine($"canonical   | {canonical}");
Console.WriteLine($"  node      | {reference.Canonical}");
Console.WriteLine($"signature   | {signature[..32]}…");
Console.WriteLine($"  node      | {reference.Signature[..32]}…");
Console.WriteLine();

var ok = true;
ok &= Check("address", identity.Address, reference.Address);
ok &= Check("canonical", canonical, reference.Canonical);
ok &= Check("signature", signature, reference.Signature);
// Round-trip: the C# verifier must accept the Node signature.
var verified = AgentIdentity.Verify(reference.Address, reference.Canonical, reference.Signature);
ok &= Report("verify(node sig)", verified);

Console.WriteLine();
Console.WriteLine(ok ? "CONFORMANCE PASS" : "CONFORMANCE FAIL");
return ok ? 0 : 1;

static bool Check(string name, string csharp, string node)
    => Report(name, string.Equals(csharp, node, StringComparison.Ordinal));

static bool Report(string name, bool pass)
{
    Console.WriteLine($"[{(pass ? "PASS" : "FAIL")}] {name}");
    return pass;
}

static Reference RunNodeReference(string seedHex)
{
    var scriptPath = Path.Combine(AppContext.BaseDirectory, "ref-sign.js");
    var psi = new ProcessStartInfo("node", $"\"{scriptPath}\" {seedHex}")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var process = Process.Start(psi)
        ?? throw new InvalidOperationException("Could not start node. Is Node.js on PATH?");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"node reference failed: {stderr}");

    using var doc = JsonDocument.Parse(stdout);
    var root = doc.RootElement;
    return new Reference(
        root.GetProperty("address").GetString()!,
        root.GetProperty("canonical").GetString()!,
        root.GetProperty("signature").GetString()!);
}

internal readonly record struct Reference(string Address, string Canonical, string Signature);

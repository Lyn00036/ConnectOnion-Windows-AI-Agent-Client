using System;
using System.Threading;
using System.Threading.Tasks;
using ConnectOnion.Protocol;

// Live end-to-end test against a deployed ConnectOnion agent (Direct URL path).
// Usage: dotnet run -- <directUrl> <address> "<prompt>"

var directUrl = args.Length > 0 ? args[0] : "http://124.156.170.117/browser";
var address = args.Length > 1 ? args[1]
    : "0xf83dfaec8890059e0bc247e78449000a4d81e808ad1d45042cfcbf28406713cb";
var prompt = args.Length > 2 ? args[2] : "Say hello to Alice in one sentence.";

Console.WriteLine($"Direct URL : {directUrl}");
Console.WriteLine($"Address    : {address}");
Console.WriteLine($"Prompt     : {prompt}");

var identity = AgentIdentity.Generate();
Console.WriteLine($"Client id  : {identity.ShortAddress}");
Console.WriteLine(new string('-', 60));

await using var connection = new AgentConnectionService(address, directUrl, identity)
{
    InviteCode = Environment.GetEnvironmentVariable("CO_INVITE_CODE") ?? "COMP9900",
};
connection.StreamEvent += e => Console.WriteLine($"  «event» {e.Type}: {e.Description}");
connection.ConnectionLost += ex => Console.WriteLine($"  «lost»  {ex.Message}");
connection.AskUserRequested += async r =>
{
    Console.WriteLine($"  «ask_user» {r.Text} (options: {string.Join(", ", r.Options)})");
    await connection.RespondAskUserAsync(r.Options.Count > 0 ? r.Options[0] : "Yes, go ahead.");
    Console.WriteLine("  «ask_user» auto-answered");
};
connection.ApprovalRequested += async r =>
{
    Console.WriteLine($"  «approval» tool={r.Tool} args={r.ArgumentsJson}");
    await connection.RespondApprovalAsync(true, "once");
    Console.WriteLine("  «approval» auto-approved once");
};
connection.PlanReviewRequested += async r =>
{
    Console.WriteLine($"  «plan_review» {r.PlanContent[..Math.Min(80, r.PlanContent.Length)]}…");
    await connection.RespondPlanReviewAsync("Plan approved. Proceed.");
    Console.WriteLine("  «plan_review» auto-approved");
};

var sessionId = Guid.NewGuid().ToString();

try
{
    // Bound the whole attempt so an onboarding gate / hang doesn't run forever.
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    Console.WriteLine("Connecting + sending input…");
    var reply = await connection.SendInputAsync(prompt, sessionId, cts.Token);

    Console.WriteLine(new string('-', 60));
    Console.WriteLine("AGENT REPLY:");
    Console.WriteLine(reply);
    Console.WriteLine(new string('-', 60));
    Console.WriteLine($"session_id : {connection.SessionId}");
    Console.WriteLine("LIVE TEST PASS");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine(new string('-', 60));
    Console.WriteLine($"LIVE TEST FAILED: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

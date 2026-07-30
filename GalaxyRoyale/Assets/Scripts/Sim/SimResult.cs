// Direct port of v1's Result type (`src/sim/types.ts`). Systems that can fail
// return SimResult so callers can distinguish OK from a reasoned failure.
namespace GalaxyRoyale.Sim
{
    public readonly struct SimResult
    {
        public bool Ok { get; }
        public string? Reason { get; }

        SimResult(bool ok, string? reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public static readonly SimResult Success = new(true, null);
        public static SimResult Fail(string reason) => new(false, reason);
    }
}

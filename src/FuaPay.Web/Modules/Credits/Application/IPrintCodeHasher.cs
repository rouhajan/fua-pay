namespace FuaPay.Web.Modules.Credits.Application;

public interface IPrintCodeHasher
{
    string Hash(string printCode);

    bool Verify(string hash, string printCode);
}

public static class PrintCodePolicy
{
    public const int Length = 6;

    public static bool IsValid(string? value) =>
        value is not null &&
        value.Length == Length &&
        value.All(character => character is >= '0' and <= '9');
}

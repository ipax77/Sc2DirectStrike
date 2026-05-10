using Sc2DirectStrike.Parser;

namespace Sc2DirectStrike.WasmSmoke;

internal static class ParserSmoke
{
    public static void VerifyParserApiIsReachable()
    {
        VerifyParse();
        VerifyParseDto();
    }

    private static void VerifyParse()
    {
        try
        {
            Sc2DirectStrikeParser.Parse(null!);
        }
        catch (ArgumentNullException)
        {
            return;
        }

        throw new InvalidOperationException("Parser smoke check expected Parse to validate a null replay.");
    }

    private static void VerifyParseDto()
    {
        try
        {
            Sc2DirectStrikeParser.ParseDto(null!);
        }
        catch (ArgumentNullException)
        {
            return;
        }

        throw new InvalidOperationException("Parser smoke check expected ParseDto to validate a null replay.");
    }
}

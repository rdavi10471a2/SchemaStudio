using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public static class UdtTypeResolver
{
    private static readonly Dictionary<string, string> Seed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["t_binary8"] = "varbinary(8)",
        ["t_bool"] = "int",
        ["t_date"] = "datetime",
        ["t_decimal"] = "decimal(9,2)",
        ["t_double"] = "float",
        ["t_int"] = "int",
        ["t_sec"] = "varbinary(64)",
        ["t_sint"] = "smallint",
        ["t_text1"] = "varchar(1)",
        ["t_text2"] = "varchar(2)",
        ["t_text3"] = "varchar(3)",
        ["t_text4"] = "varchar(4)",
        ["t_text5"] = "varchar(5)",
        ["t_text6"] = "varchar(6)",
        ["t_text8"] = "varchar(8)",
        ["t_text9"] = "varchar(9)",
        ["t_text10"] = "varchar(10)",
        ["t_text11"] = "varchar(11)",
        ["t_text12"] = "varchar(12)",
        ["t_text15"] = "varchar(15)",
        ["t_text16"] = "varchar(16)",
        ["t_text17"] = "varchar(17)",
        ["t_text20"] = "varchar(20)",
        ["t_text24"] = "varchar(24)",
        ["t_text25"] = "varchar(25)",
        ["t_text28"] = "varchar(28)",
        ["t_text30"] = "varchar(30)",
        ["t_text35"] = "varchar(35)",
        ["t_text40"] = "varchar(40)",
        ["t_text50"] = "varchar(50)",
        ["t_text100"] = "varchar(100)",
        ["t_text150"] = "varchar(150)",
        ["t_text255"] = "varchar(255)",
        ["t_text500"] = "varchar(500)",
        ["t_text1000"] = "varchar(1000)",
        ["t_text1500"] = "varchar(1500)",
        ["t_text2000"] = "varchar(2000)",
        ["t_text3000"] = "varchar(3000)",
        ["t_text4000"] = "varchar(4000)",
        ["t_textAcjeitmCtrl"] = "varchar(40)",
        ["t_textAcjeitmDes"] = "varchar(150)",
        ["t_textAcjeitmXref"] = "varchar(50)",
        ["t_textCobrndcsDes"] = "varchar(280)",
        ["t_textPttapDes"] = "varchar(50)",
        ["t_textSqlStmt"] = "varchar(8000)",
        ["t_textState"] = "varchar(3)",
        ["t_textVhFeaId"] = "varchar(15)",
        ["t_textVhvinModelNbr"] = "varchar(20)"
    };

    private static readonly Regex TextAlias =
        new("^t_text(?<len>[0-9]{1,4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ResolveByName(string? declaredType)
    {
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return declaredType ?? "";
        }

        if (Seed.TryGetValue(declaredType, out var seeded))
        {
            return seeded;
        }

        var match = TextAlias.Match(declaredType);
        if (match.Success)
        {
            return $"varchar({match.Groups["len"].Value})";
        }

        return declaredType;
    }
}

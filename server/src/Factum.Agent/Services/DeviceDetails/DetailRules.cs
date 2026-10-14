using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Factum.Agent.Models;

namespace Factum.Agent.Services.DeviceDetails;

/// <summary>
/// Validación y saneamiento de lo que devuelve el dispositivo (metadatos-dispositivo §4.1/§4.5). Es la misma regla
/// que <c>DeviceDetailsRules</c> de la API (se prueban con la misma tabla). Un valor que no cumple pasa a
/// <c>invalid</c> y no se guarda ni se loguea.
/// </summary>
public static partial class DetailRules
{
    /// <summary>Texto libre (§4.5 punto 4): como máximo 64 elementos de texto; no se recorta.</summary>
    internal const int FreeTextMax = 64;

    private static readonly Dictionary<string, Regex> Patterns = new(StringComparer.Ordinal)
    {
        [DetailKeys.SerialNumber] = new(@"^[A-Za-z0-9._:-]{1,64}$", RegexOptions.CultureInvariant),
        [DetailKeys.OsVersion] = new(@"^[0-9A-Za-z.]{1,16}$", RegexOptions.CultureInvariant),
        [DetailKeys.OsApiLevel] = new(@"^[0-9]{1,3}$", RegexOptions.CultureInvariant),
        [DetailKeys.BuildId] = new(@"^[\x21-\x7E]{1,64}$", RegexOptions.CultureInvariant),
        [DetailKeys.BuildFingerprint] = new(@"^[\x21-\x7E]{1,200}$", RegexOptions.CultureInvariant),
        [DetailKeys.SecurityPatch] = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant),
        [DetailKeys.Kernel] = new(@"^[\x20-\x7E]{1,300}$", RegexOptions.CultureInvariant),
        [DetailKeys.NetworkOperatorNumeric] = new(@"^[0-9,]{1,32}$", RegexOptions.CultureInvariant),
        [DetailKeys.SimOperatorNumeric] = new(@"^[0-9,]{1,32}$", RegexOptions.CultureInvariant),
        [DetailKeys.SimState] = new(@"^[A-Za-z_,]{1,64}$", RegexOptions.CultureInvariant),
        [DetailKeys.VerifiedBootState] = new(@"^[a-z]{1,16}$", RegexOptions.CultureInvariant),
        [DetailKeys.BootloaderLocked] = new(@"^[01]$", RegexOptions.CultureInvariant),
        [DetailKeys.VbmetaDeviceState] = new(@"^[a-z]{1,16}$", RegexOptions.CultureInvariant),
        [DetailKeys.Timezone] = new(@"^[A-Za-z0-9_+\-/]{1,64}$", RegexOptions.CultureInvariant),
        [DetailKeys.AutoTime] = new(@"^[01]$", RegexOptions.CultureInvariant),
        [DetailKeys.AutoTimeZone] = new(@"^[01]$", RegexOptions.CultureInvariant),
        [DetailKeys.WifiState] = new(@"^(connected|disconnected|disabled)$", RegexOptions.CultureInvariant),
    };

    private static readonly HashSet<string> FreeTextKeys =
        new([DetailKeys.NetworkOperator, DetailKeys.SimOperator, DetailKeys.WifiSsid], StringComparer.Ordinal);

    /// <summary>
    /// Valor crudo → <see cref="DetailField"/>: limpia (§4.5.1), vacío → <c>empty</c>, regex/longitud → <c>invalid</c>.
    /// <c>unknown</c> en <c>serial_number</c> y <c>null</c> en <c>auto_time*</c> → <c>empty</c>; <c>&lt;unknown ssid&gt;</c>
    /// en <c>wifi_ssid</c> → <c>masked</c>. Una clave fuera de las 20 → <c>error</c>.
    /// </summary>
    public static DetailField Normalize(string key, string? raw)
    {
        var v = Clean(raw);
        if (v.Length == 0) return DetailField.Missing(DetailReasons.Empty);

        switch (key)
        {
            case DetailKeys.SerialNumber when string.Equals(v, "unknown", StringComparison.OrdinalIgnoreCase):
            case DetailKeys.AutoTime or DetailKeys.AutoTimeZone when v == "null":
                return DetailField.Missing(DetailReasons.Empty);
            case DetailKeys.WifiSsid when v == "<unknown ssid>":
                return DetailField.Missing(DetailReasons.Masked);
        }

        if (FreeTextKeys.Contains(key))
            return new StringInfo(v).LengthInTextElements <= FreeTextMax
                ? DetailField.Of(v)
                : DetailField.Missing(DetailReasons.Invalid);

        if (!Patterns.TryGetValue(key, out var re)) return DetailField.Missing(DetailReasons.Error);
        if (!re.IsMatch(v)) return DetailField.Missing(DetailReasons.Invalid);
        if (key == DetailKeys.SecurityPatch &&
            !DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return DetailField.Missing(DetailReasons.Invalid);
        return DetailField.Of(v);
    }

    /// <summary>
    /// Equivalente a <c>SafeText.Clean(v, TextShape.SingleLine)</c> de la API, más <c>Trim()</c> y colapso de
    /// espacios: los C0 se quitan (saltos y tab → espacio), los bidi U+202A–U+202E y U+2066–U+2069, los
    /// no-caracteres y los surrogates sueltos también. DEL y C1 se conservan (como en la API).
    /// </summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    var cp = char.ConvertToUtf32(ch, value[i + 1]);
                    if (!IsNonCharacter(cp)) sb.Append(ch).Append(value[i + 1]);
                    i++;
                }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            if (ch == '\r')
            {
                sb.Append(' ');
                if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                continue;
            }
            if (ch is '\n' or '\t') { sb.Append(' '); continue; }
            if (ch < 0x20 || IsBidiControl(ch) || IsNonCharacter(ch)) continue;
            sb.Append(ch);
        }
        return MultiSpace().Replace(sb.ToString().Trim(), " ");
    }

    private static bool IsBidiControl(int cp) => cp is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;

    private static bool IsNonCharacter(int cp) => cp is >= 0xFDD0 and <= 0xFDEF || (cp & 0xFFFE) == 0xFFFE;

    [GeneratedRegex(" {2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();
}

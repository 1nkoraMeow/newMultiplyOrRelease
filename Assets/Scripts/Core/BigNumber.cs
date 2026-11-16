using System;
using System.Globalization;
using System.Text;

namespace MultiplyOrRelease
{
    /// <summary>
    /// 大数显示。三套格式：
    ///   · Grouped         —— 子弹数量：三位分级 + 空格分隔，2531742 → "2 531 742"
    ///   · Suffixed        —— 大球 / 护盾：1,0M、269M、8,4M（尾数小于 10 保留一位小数）
    ///   · SuffixedRounded —— 增倍球：四舍五入到整数，33554432 → "34M"、16777216 → "17M"
    /// </summary>
    public static class BigNumber
    {
        static readonly string[] Suffixes = { "", "K", "M", "B", "T", "P", "E" };

        /// <summary>三位分级 + 空格分隔（规格：2531742 → "2 531 742"）。</summary>
        public static string Grouped(double value)
        {
            if (double.IsNaN(value)) return "0";
            if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";

            double a = Math.Abs(value);
            if (a >= 9.0e18) return Format(value, false);   // 超出 long 范围就用后缀写法兜底

            long n = (long)Math.Round(a);
            string digits = n.ToString(CultureInfo.InvariantCulture);
            char sep = Cfg.GroupSeparator;

            var sb = new StringBuilder(digits.Length + digits.Length / 3 + 1);
            int lead = digits.Length % 3;
            if (lead == 0) lead = 3;
            sb.Append(digits, 0, lead);
            for (int i = lead; i < digits.Length; i += 3)
            {
                sb.Append(sep);
                sb.Append(digits, i, 3);
            }
            return (value < 0 ? "-" : "") + sb.ToString();
        }

        /// <summary>
        /// 把数字拆成「三位一组」的数组，用于录像里那种**换行显示**：
        /// 2531742 → { "2", "531", "742" }，一行一组、居中、整块贴底。
        /// </summary>
        public static string[] Groups(double value)
        {
            if (double.IsNaN(value)) return new[] { "0" };
            double a = Math.Abs(value);
            if (a >= 9.0e18) return new[] { Suffixed(value) };

            string digits = ((long)Math.Round(a)).ToString(CultureInfo.InvariantCulture);
            int n = (digits.Length + 2) / 3;
            var groups = new string[n];
            int first = digits.Length % 3;
            if (first == 0) first = 3;
            groups[0] = (value < 0 ? "-" : "") + digits.Substring(0, first);
            int idx = 1;
            for (int i = first; i < digits.Length; i += 3)
                groups[idx++] = digits.Substring(i, 3);
            return groups;
        }

        /// <summary>后缀写法，尾数小于 10 时保留一位小数：1,0M / 8,4M / 269M / 33K。</summary>
        public static string Suffixed(double value)
        {
            return Format(value, false);
        }

        /// <summary>
        /// 后缀写法，K 及以上一律四舍五入到整数。
        /// 规格要求：增倍球用这个，33554432 → "34M"，16777216 → "17M"，4096 → "4K"。
        /// </summary>
        public static string SuffixedRounded(double value)
        {
            return Format(value, true);
        }

        static string Format(double value, bool rounded)
        {
            if (double.IsNaN(value)) return "0";
            if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";

            double a = Math.Abs(value);
            string sign = value < 0 ? "-" : "";

            int tier = 0;
            double mantissa = a;
            while (mantissa >= 1000.0 && tier < Suffixes.Length - 1)
            {
                mantissa /= 1000.0;
                tier++;
            }

            if (tier == 0)
            {
                // 1000 以下：小于 10 且有小数才保留一位，否则取整
                if (a < 10.0 && a != Math.Floor(a) && !rounded) return sign + Num(mantissa, 1);
                // ★ 进位修正：`999,6` 会被写成 "1000"（还没后缀）→ 进位成 "1,0K"
                if (Math.Round(a) >= 1000.0)
                    return sign + Num(1.0, (!rounded && Cfg.ShowOneDecimalUnder10) ? 1 : 0) + Suffixes[1];
                return sign + Num(mantissa, 0);
            }

            // ★ 规格（README 第 191 行）：尾数**小于 10 一律保留一位** —— 1,0M / 8,4M。
            //   以前这里多了一个 `mantissa != Math.Floor(mantissa)`，于是 1M 被写成 "1M"，
            //   而进位修正那条路（999,6M → 1,0B）却还是 "1,0B" —— 同一个 1.0 出现两种写法。
            //   现在统一成「小于 10 就带一位」，和 `ShowOneDecimalUnder10` 的注释一致。
            int decimals = (!rounded && Cfg.ShowOneDecimalUnder10 && mantissa < 10.0) ? 1 : 0;
            // ★★ 进位修正：`999,6M` 四舍五入后会被写成 "1000M" —— 必须进到下一档 "1,0B"。
            //   原来只按「未取整的 mantissa ≥ 1000」换档，于是 999,5M~999,99M 这一段
            //   全都显示成 "1000M"（用户看到的那个）。
            if (tier < Suffixes.Length - 1 && Math.Round(mantissa, decimals) >= 1000.0)
            {
                mantissa = 1.0;
                tier++;
                decimals = (!rounded && Cfg.ShowOneDecimalUnder10) ? 1 : 0;
            }
            if (decimals == 1 && Math.Round(mantissa, 1) >= 10.0) decimals = 0;
            return sign + Num(mantissa, decimals) + Suffixes[tier];
        }

        static string Num(double v, int decimals)
        {
            string s = v.ToString(decimals == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
            if (Cfg.StripTrailingZeroDecimal && s.EndsWith(".0", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 2);
            if (Cfg.DecimalSeparator != '.') s = s.Replace('.', Cfg.DecimalSeparator);
            return s;
        }
    }
}

using System.Text;

namespace SmarcivaZip.Core.Encodings;

public sealed record EncodingGuess(CodePageInfo CodePage, double Score, bool IsDecodable)
{
    public double Confidence { get; init; }
}

public sealed record EncodingDetectionResult(
    int CodePage,
    double Confidence,
    IReadOnlyList<EncodingGuess> Ranked,
    bool AllAscii)
{
    /// <summary>
    /// ASCII のみ、または判定に十分な自信があるなら何も聞かずに展開してよい。
    /// それ以外は確認ダイアログを出す価値がある。
    /// </summary>
    public bool NeedsUserConfirmation => !AllAscii && Confidence < 0.80;
}

/// <summary>
/// ファイル名の生バイト列からコードページを推定する。
///
/// ZIP は「UTF-8 である」というフラグ (汎用目的ビット 11) がある場合を除き、
/// どの文字コードで書かれたかを一切記録しない。そのため統計的に当てるしかない。
///
/// 判定は 3 段階で行う。
///   1. そのコードページでデコードできるか（できなければ即除外）
///   2. デコード結果の文字種が、そのコードページの言語圏として自然か
///   3. 生バイト列が、その言語で実際によく使われる領域に収まっているか
///
/// 3 が効くのが肝。たとえば CP932 の日本語を CP949 で読むと一応ハングルに
/// 「なってしまう」が、そのハングルは常用外の領域に散らばる。
/// 逆に本物の韓国語は EUC-KR の完成型常用領域 (0xB0A1-0xC8FE) にほぼ収まる。
/// 文字種だけを見ていると、この 2 つを取り違える。
/// </summary>
public static class EncodingDetector
{
    /// <summary>同点のときに優先する言語圏。既定は日本語（CP932）。</summary>
    public static int PreferredCodePage { get; set; } = CodePageInfo.ShiftJis;

    public static EncodingDetectionResult Detect(IEnumerable<byte[]> nameByteSequences)
    {
        var samples = nameByteSequences.Where(b => b.Length > 0).ToList();

        if (samples.Count == 0 || samples.All(s => IsPureAscii(s)))
        {
            return new EncodingDetectionResult(CodePageInfo.Utf8, 1.0, [], AllAscii: true);
        }

        var guesses = new List<EncodingGuess>();

        foreach (CodePageInfo candidate in CodePageInfo.Candidates)
        {
            Encoding? strict = CodePageInfo.GetStrictEncoding(candidate.CodePage);
            if (strict is null) continue;

            double total = 0;
            int decodableCount = 0;
            int nonAsciiSamples = 0;

            foreach (byte[] sample in samples)
            {
                if (IsPureAscii(sample)) continue;
                nonAsciiSamples++;

                string? decoded = TryDecode(strict, sample);
                if (decoded is null)
                {
                    // このコードページでは表現できないバイト列があった。ほぼ確実に違う。
                    total -= 40;
                    continue;
                }

                decodableCount++;
                total += ScoreDecodedName(decoded, candidate.CodePage);
                total += ScoreByteStructure(sample, candidate.CodePage);
            }

            if (nonAsciiSamples == 0) continue;

            bool fullyDecodable = decodableCount == nonAsciiSamples;
            double normalized = total / nonAsciiSamples;

            normalized += Prior(candidate.CodePage);
            if (candidate.CodePage == PreferredCodePage) normalized += 0.35;

            guesses.Add(new EncodingGuess(candidate, normalized, fullyDecodable));
        }

        if (guesses.Count == 0)
        {
            return new EncodingDetectionResult(CodePageInfo.Utf8, 0.0, [], AllAscii: false);
        }

        List<EncodingGuess> ranked = guesses
            .OrderByDescending(g => g.IsDecodable)
            .ThenByDescending(g => g.Score)
            .ToList();

        EncodingGuess best = ranked[0];
        EncodingGuess? runnerUp = ranked.Count > 1 ? ranked[1] : null;

        double confidence = ComputeConfidence(best, runnerUp);
        ranked = ranked.Select(g => g with { Confidence = g == best ? confidence : 0 }).ToList();

        return new EncodingDetectionResult(best.CodePage.CodePage, confidence, ranked, AllAscii: false);
    }

    private static double ComputeConfidence(EncodingGuess best, EncodingGuess? runnerUp)
    {
        if (!best.IsDecodable) return 0.2;

        // 非 ASCII のバイト列が偶然 UTF-8 として厳密に妥当になることはまず無いので、
        // UTF-8 が勝った時点でほぼ確定してよい。
        if (best.CodePage.CodePage == CodePageInfo.Utf8 && best.Score > 0) return 0.97;

        if (runnerUp is null || !runnerUp.IsDecodable) return 0.9;
        if (best.Score <= 0) return 0.25;

        double gap = best.Score - runnerUp.Score;

        // 差が 2.0 以上開いていれば実用上ほぼ確定と見てよい。
        return Math.Clamp(0.5 + gap / 4.0, 0.25, 0.95);
    }

    /// <summary>
    /// 実際に出回っているアーカイブでの出現頻度を反映した事前確率。
    /// DOS 時代の単バイトコードページは、他の候補と同点なら選ばれてほしくない。
    /// たとえば "cafe" のアクサン付き e は CP866 ではキリル文字にも読めてしまうが、
    /// 現実にはラテン系である方がはるかに多い。
    /// </summary>
    private static double Prior(int codePage) => codePage switch
    {
        CodePageInfo.Oem437 => -0.8,
        CodePageInfo.Cyrillic866 => -0.3,
        _ => 0.0
    };

    public static bool IsPureAscii(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (b >= 0x80) return false;
        }
        return true;
    }

    private static string? TryDecode(Encoding strict, byte[] bytes)
    {
        try { return strict.GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }

    /// <summary>
    /// デコード結果が「そのコードページの言語のファイル名としてありそうか」を採点する。
    /// 非 ASCII 1 文字あたりの平均点を返す。
    /// </summary>
    private static double ScoreDecodedName(string text, int codePage)
    {
        double score = 0;
        int scoredChars = 0;

        foreach (char c in text)
        {
            // ASCII 部分はどのコードページでも同じ結果になるので判定材料にならない。
            if (c < 0x80) continue;

            scoredChars++;
            score += ScoreChar(c, codePage);
        }

        return scoredChars == 0 ? 0 : score / scoredChars;
    }

    private static double ScoreChar(char c, int codePage)
    {
        // 制御文字・置換文字はファイル名にまず出てこない。
        if (char.IsControl(c) || c == (char)0xFFFD) return -10;
        if (char.IsSurrogate(c)) return -1;

        // 私用領域が出たら、そのコードページでの解釈はほぼ間違い。
        if (c is >= (char)0xE000 and <= (char)0xF8FF) return -10;

        // 未定義・非文字。
        if (c is >= (char)0xFFF0 and <= (char)0xFFFF) return -10;

        return c switch
        {
            // ひらがな・カタカナ。日本語ファイル名で圧倒的によく出る。
            >= (char)0x3040 and <= (char)0x30FF => codePage == CodePageInfo.ShiftJis ? 2.5 : 0.5,

            // 半角カタカナ。古い日本語 ZIP に残っていることもあるが、
            // 他言語の 2 バイト文字を CP932 で誤読したときにも大量に出る。
            // 連続具合は ScoreByteStructure 側で見るので、ここでは控えめに。
            >= (char)0xFF61 and <= (char)0xFF9F => codePage == CodePageInfo.ShiftJis ? 0.5 : 0.0,

            // ハングル音節。
            >= (char)0xAC00 and <= (char)0xD7A3 => codePage == CodePageInfo.EucKr ? 2.0 : 0.2,

            // ハングル字母単体。正規のファイル名にはほぼ出ない。
            >= (char)0x3130 and <= (char)0x318F => -1.0,

            // CJK 統合漢字。日中では常用、韓国語のファイル名では稀。
            >= (char)0x4E00 and <= (char)0x9FFF => codePage switch
            {
                CodePageInfo.ShiftJis or CodePageInfo.Gbk or CodePageInfo.Big5 => 1.5,
                CodePageInfo.Utf8 => 1.5,
                CodePageInfo.EucKr => 0.3,
                _ => 0.5
            },

            // CJK 拡張 A。常用ファイル名には出ない＝誤読のサイン。
            >= (char)0x3400 and <= (char)0x4DBF => -1.5,

            // CJK 部首・康熙部首。ほぼ誤読でしか出ない。
            >= (char)0x2E80 and <= (char)0x2FDF => -3.0,

            // 全角英数・全角記号。
            >= (char)0xFF01 and <= (char)0xFF60 => 1.0,

            // CJK 記号・句読点。
            >= (char)0x3000 and <= (char)0x303F => 1.0,

            // キリル文字。
            >= (char)0x0400 and <= (char)0x04FF => codePage == CodePageInfo.Cyrillic866 ? 1.5 : 0.3,

            // ラテン文字拡張（アクセント付き）。
            >= (char)0x00C0 and <= (char)0x024F =>
                codePage is CodePageInfo.Latin1252 or CodePageInfo.Oem437 or CodePageInfo.Utf8 ? 1.2 : -0.5,

            // ラテン 1 の記号。ファイル名にたまに出る程度。
            >= (char)0x00A0 and <= (char)0x00BF => 0.0,

            // 罫線素片。CP437 / CP866 での誤読で大量発生する典型パターン。
            >= (char)0x2500 and <= (char)0x257F => -4.0,

            // その他の記号・絵文字。UTF-8 のファイル名では普通に出る。
            >= (char)0x2000 and <= (char)0x2BFF => codePage == CodePageInfo.Utf8 ? 0.8 : -1.0,

            _ => 0.0
        };
    }

    /// <summary>
    /// 生バイト列が、そのコードページの「実際によく使われる領域」に
    /// 収まっているかを見る。文字種だけでは区別できない
    /// CP932 / CP936 / CP949 / CP950 の取り違えを、ここで断ち切る。
    /// </summary>
    private static double ScoreByteStructure(byte[] bytes, int codePage) => codePage switch
    {
        // 非 ASCII を含みながら UTF-8 として厳密に妥当、というのは偶然では起きない。
        // ここに到達している時点でデコードは成功しているので、強い加点を与える。
        CodePageInfo.Utf8 => 4.0,

        CodePageInfo.ShiftJis => ScoreShiftJisBytes(bytes),

        CodePageInfo.Gbk => ScoreDoubleByteRange(bytes, 0x81, 0xFE,
            common: (0xB0A1, 0xD7F9), secondary: (0xD8A1, 0xF7FE), symbols: (0xA1A1, 0xA9FE)),

        CodePageInfo.EucKr => ScoreDoubleByteRange(bytes, 0x81, 0xFE,
            common: (0xB0A1, 0xC8FE), secondary: (0xCAA1, 0xFDFE), symbols: (0xA1A1, 0xACFE)),

        CodePageInfo.Big5 => ScoreDoubleByteRange(bytes, 0xA1, 0xF9,
            common: (0xA440, 0xC67E), secondary: (0xC940, 0xF9D5), symbols: (0xA140, 0xA3BF)),

        // 単バイトのコードページは構造から得られる情報が無い。
        _ => 0.0
    };

    /// <summary>
    /// CP932 固有の判定。半角カタカナ（単バイト 0xA1-0xDF）が名前の大半を
    /// 占めていたら、それは他言語の 2 バイト文字を誤読した結果とみなす。
    /// </summary>
    private static double ScoreShiftJisBytes(byte[] bytes)
    {
        int halfWidthKana = 0;
        int units = 0;
        double score = 0;

        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b < 0x80) continue;

            units++;

            if (b is >= 0xA1 and <= 0xDF)
            {
                halfWidthKana++;
                continue;
            }

            if (i + 1 >= bytes.Length)
            {
                score -= 1.0;
                continue;
            }

            int pair = (b << 8) | bytes[i + 1];
            i++;

            score += pair switch
            {
                >= 0x829F and <= 0x83D6 => 2.0,  // ひらがな・カタカナ
                >= 0x8140 and <= 0x81FC => 1.0,  // 記号・約物
                >= 0x8840 and <= 0x9872 => 1.5,  // 第 1 水準漢字（日常的な漢字）
                >= 0x989F and <= 0xEAA4 => 0.2,  // 第 2 水準漢字（多用は誤読の疑い）
                _ => -1.0
            };
        }

        if (units == 0) return 0;

        double average = score / units;

        // 半角カタカナばかりの名前は、EUC-KR や GBK を誤読したときの典型的な姿。
        if (halfWidthKana * 2 > units) average -= 2.5;

        return average;
    }

    /// <summary>
    /// 2 バイトコードページ共通の判定。
    /// 常用領域に収まっていれば加点、外れていれば減点する。
    /// </summary>
    private static double ScoreDoubleByteRange(
        byte[] bytes, byte leadMin, byte leadMax,
        (int Low, int High) common, (int Low, int High) secondary, (int Low, int High) symbols)
    {
        int units = 0;
        double score = 0;

        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b < 0x80) continue;

            units++;

            if (b < leadMin || b > leadMax || i + 1 >= bytes.Length)
            {
                score -= 1.0;
                continue;
            }

            int pair = (b << 8) | bytes[i + 1];
            i++;

            if (pair >= common.Low && pair <= common.High) score += 2.0;
            else if (pair >= symbols.Low && pair <= symbols.High) score += 1.0;
            else if (pair >= secondary.Low && pair <= secondary.High) score += 0.3;
            else score -= 1.0;
        }

        return units == 0 ? 0 : score / units;
    }
}

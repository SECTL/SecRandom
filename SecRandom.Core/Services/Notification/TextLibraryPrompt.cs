using System.Globalization;

namespace SecRandom.Core.Services.Notification;

/// <summary>
///     文案库导入抽屉里可一键复制的 AI 提示词。
/// </summary>
/// <remarks>
///     <para>
///         提示词本身固定为英文，只有"用哪种语言回复我 / 用哪种语言写文案"这两句跟随软件界面语言，
///         因为用户会把整段发给任意对话式 AI，英文指令的通用性最好。两句必须都带上语言：
///         只写文案语言的话，AI 仍会用英文提问和解释，用户看到的就是"软件是中文、AI 却全程英文"。
///     </para>
///     <para>
///         输出约束必须与 <see cref="TextLibraryImporter" /> 的校验保持一致（JSON 字符串数组、
///         单条 ≤ <see cref="TextLibraryImporter.MaxTextLength" /> 字符、<c>{result}</c> 占位符只出现一次、
///         整体不超过 <see cref="TextLibraryImporter.MaxJsonImportChars" /> 字符），
///         否则 AI 生成的数组无法直接粘贴回导入框；<c>Template</c> 段给出的示例本身也必须能通过导入解析。
///     </para>
/// </remarks>
public static class TextLibraryPrompt
{
    private const string OutputLanguageToken = "__OUTPUT_LANGUAGE__";

    private const string Template =
        """
        I am preparing the text library of SecRandom, a random draw app. Each entry is a short text that the app shows as a fullscreen result message, and its {result} placeholder is replaced with the drawn name or prize.

        Reply to me in __OUTPUT_LANGUAGE__. Before writing anything, ask me these two questions and wait for my answers:
        1. What kind of texts do you need?
        2. How many texts do you need? (1 to 500)

        After I answer, write exactly that many texts in __OUTPUT_LANGUAGE__. Follow these format rules:
        - Reply with exactly one fenced code block tagged json, and put nothing outside the code block
        - The code block contains one JSON array of strings, following the template below
        - Every item is one single-line text with no numbering or list markers
        - Every item is 2 to 200 characters, and the whole array stays under 14000 characters
        - Put the {result} placeholder exactly once in every item, where the drawn result belongs
        - Do not ask more questions once you know the kind and the count

        Template:
        ```json
        [
          "Example text with {result}",
          "Another example with {result}"
        ]
        ```

        Replace the two example texts with the requested content and keep the JSON valid
        """;

    /// <summary>
    ///     按软件界面语言生成提示词：正文永远是英文，只有回复语言与文案语言两处语言名随界面语言变化。
    /// </summary>
    /// <param name="culture">界面语言；为空或不受支持时按英文输出。</param>
    public static string Build(CultureInfo? culture) =>
        Template.Replace(OutputLanguageToken, OutputLanguageName(culture), StringComparison.Ordinal);

    private static string OutputLanguageName(CultureInfo? culture) => culture?.TwoLetterISOLanguageName switch
    {
        "zh" => "Simplified Chinese",
        "ja" => "Japanese",
        _ => "English"
    };
}
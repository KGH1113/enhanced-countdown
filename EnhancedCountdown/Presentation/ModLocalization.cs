using TMPro;
using UnityEngine;

namespace EnhancedCountdown.Presentation;

internal enum ModLocale
{
  English,
  Korean,
  ChineseSimplified,
  ChineseTraditional,
}

internal enum ModText
{
  Updates,
  ReceiveBetaUpdates,
  BetaWarning,
  ClickBpm,
  Volume,
  TimeSignature,
}

internal static class ModLocalization
{
  internal static ModLocale CurrentLocale
  {
    get
    {
      try
      {
        RDString.Setup();
        return FromSystemLanguage(RDString.language);
      }
      catch
      {
        return ModLocale.English;
      }
    }
  }

  internal static string Get(ModText text, ModLocale locale)
  {
    return locale switch
    {
      ModLocale.Korean => GetKorean(text),
      ModLocale.ChineseSimplified => GetChineseSimplified(text),
      ModLocale.ChineseTraditional => GetChineseTraditional(text),
      _ => GetEnglish(text),
    };
  }

  internal static Font GetLegacyFont(ModLocale locale)
  {
    SystemLanguage? language = GetCjkSystemLanguage(locale);
    return language.HasValue ? RDString.GetFontDataForLanguage(language.Value).font : null;
  }

  internal static void ApplyTmpFont(TMP_Text text, ModLocale locale)
  {
    SystemLanguage? language = GetCjkSystemLanguage(locale);
    if (text == null || !language.HasValue)
    {
      return;
    }

    FontData fontData = RDString.GetFontDataForLanguage(language.Value);
    if (fontData.fontTMP != null)
    {
      text.font = fontData.fontTMP;
      text.lineSpacing *= fontData.lineSpacingTMP;
    }
  }

  private static ModLocale FromSystemLanguage(SystemLanguage language)
  {
    return language switch
    {
      SystemLanguage.Korean => ModLocale.Korean,
      SystemLanguage.Chinese => ModLocale.ChineseSimplified,
      SystemLanguage.ChineseSimplified => ModLocale.ChineseSimplified,
      SystemLanguage.ChineseTraditional => ModLocale.ChineseTraditional,
      _ => ModLocale.English,
    };
  }

  private static SystemLanguage? GetCjkSystemLanguage(ModLocale locale)
  {
    return locale switch
    {
      ModLocale.Korean => SystemLanguage.Korean,
      ModLocale.ChineseSimplified => SystemLanguage.ChineseSimplified,
      ModLocale.ChineseTraditional => SystemLanguage.ChineseTraditional,
      _ => null,
    };
  }

  private static string GetEnglish(ModText text)
  {
    return text switch
    {
      ModText.Updates => "Updates",
      ModText.ReceiveBetaUpdates => "Receive beta updates",
      ModText.BetaWarning => "Beta builds may be unstable. Changes apply on the next game launch.",
      ModText.ClickBpm => "CLICK BPM",
      ModText.Volume => "VOLUME",
      ModText.TimeSignature => "TIME SIGNATURE",
      _ => string.Empty,
    };
  }

  private static string GetKorean(ModText text)
  {
    return text switch
    {
      ModText.Updates => "업데이트",
      ModText.ReceiveBetaUpdates => "베타 업데이트 받기",
      ModText.BetaWarning => "베타 빌드는 불안정할 수 있습니다. 변경 사항은 다음 게임 실행 시 적용됩니다.",
      ModText.ClickBpm => "클릭 BPM",
      ModText.Volume => "음량",
      ModText.TimeSignature => "박자표",
      _ => string.Empty,
    };
  }

  private static string GetChineseSimplified(ModText text)
  {
    return text switch
    {
      ModText.Updates => "更新",
      ModText.ReceiveBetaUpdates => "接收测试版更新",
      ModText.BetaWarning => "测试版可能不稳定。更改将在下次启动游戏时生效。",
      ModText.ClickBpm => "节拍 BPM",
      ModText.Volume => "音量",
      ModText.TimeSignature => "拍号",
      _ => string.Empty,
    };
  }

  private static string GetChineseTraditional(ModText text)
  {
    return text switch
    {
      ModText.Updates => "更新",
      ModText.ReceiveBetaUpdates => "接收測試版更新",
      ModText.BetaWarning => "測試版可能不穩定。變更將在下次啟動遊戲時生效。",
      ModText.ClickBpm => "節拍 BPM",
      ModText.Volume => "音量",
      ModText.TimeSignature => "拍號",
      _ => string.Empty,
    };
  }
}

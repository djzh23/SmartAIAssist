namespace CvStudio.Infrastructure.Pdf.Designs.DesignC;

public static class DesignCStyles
{
    // Farben Sidebar
    public const string SidebarBg = "#FFFFFF";
    public const string SidebarText = "#1F2937";
    public const string SidebarMuted = "#6B7280";
    public const string Cyan = "#00BCD4";
    public const string CyanDark = "#0097A7";
    public const string BlobColor = "#DBEAFE";
    public const string BlobAccentDark = "#1D4ED8";
    public const string IconBadgeBg = "#374151";
    public const string IconBadgeText = "#FFFFFF";
    public const string CyanBadge = "#06B6D4";
    public const string CyanBadgeText = "#FFFFFF";
    public const string DotFilled = "#3B82F6";
    public const string DotEmpty = "#D1D5DB";

    // Farben Main
    public const string MainBg = "#FFFFFF";
    public const string MainText = "#0F1C2E";
    /// <summary> Sekundaertext: etwas dunkler als frueher fuer bessere Lesbarkeit im PDF. </summary>
    public const string MainMuted = "#4B5563";
    public const string SectionLine = "#E5E7EB";
    public const string BulletColor = "#374151";

    // Schriftgroessen (Professional: gut lesbar, Seite wirkt gefuellt)
    public const float NameSize = 29f;
    public const float HeadlineSize = 11f;
    public const float SectionTitle = 10f;
    public const float BodyText = 9.25f;
    public const float SmallText = 8.25f;
    public const float SidebarLabel = 8.25f;
    public const float SidebarBody = 9f;
    public const float HeaderContactSize = 8.75f;
    public const float SectionIconSize = 9f;
    public const float SidebarIconBadge = 8f;

    // Abstaende
    public const float SectionGap = 8f;
    public const float ItemGap = 6f;
    public const float BulletIndent = 9f;
    public const float SidebarWidth = 172f;
    /// <summary> Feste Breite fuer die 5-Sterne-Anzeige neben dem Sprachnamen (verhindert gequetschten Text). </summary>
    public const float LanguageDotsColumnWidth = 46f;
}

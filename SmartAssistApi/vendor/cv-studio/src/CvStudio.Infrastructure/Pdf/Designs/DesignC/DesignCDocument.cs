using System.Globalization;
using CvStudio.Application.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CvStudio.Infrastructure.Pdf.Designs.DesignC;

public sealed class DesignCDocument : IDocument
{
    private readonly ResumeData _data;
    private readonly byte[]? _profileImageBytes;
    private readonly ProfileData _profile;
    private readonly IReadOnlyList<WorkItemData> _workItems;
    private readonly IReadOnlyList<EducationItemData> _educationItems;
    private readonly IReadOnlyList<ResumeProjectItem> _projects;
    private readonly IReadOnlyList<SkillGroupData> _skills;

    public DesignCDocument(ResumeData data, byte[]? profileImageBytes)
    {
        _data = data ?? new ResumeData();
        _profileImageBytes = profileImageBytes;
        _profile = _data.Profile ?? new ProfileData();
        _workItems = _data.WorkItems ?? [];
        _educationItems = _data.EducationItems ?? [];
        _projects = _data.Projects ?? [];
        _skills = _data.Skills ?? [];
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x =>
                x.FontFamily("Arial")
                 .FontSize(DesignCStyles.BodyText)
                 .FontColor(DesignCStyles.MainText)
                 .LineHeight(1.24f));

            page.Content().Row(row =>
            {
                row.ConstantItem(DesignCStyles.SidebarWidth)
                    .Background(DesignCStyles.SidebarBg)
                    .Column(ComposeSidebar);

                row.RelativeItem()
                    .Background(DesignCStyles.MainBg)
                    .Column(ComposeMain);
            });
        });
    }

    private void ComposeSidebar(ColumnDescriptor col)
    {
        col.Item().Height(12);
        ComposePhotoWithAccent(col);
        col.Item().Height(10);

        ComposeSidebarSectionLabel(col, "\u2709", CvSectionTitleResolver.Contacts(_data));
        col.Item().PaddingHorizontal(16).Column(inner =>
        {
            if (!string.IsNullOrWhiteSpace(_profile.Phone))
            {
                inner.Item().Text(_profile.Phone.Trim())
                    .FontSize(DesignCStyles.SidebarBody)
                    .FontColor(DesignCStyles.SidebarText);
                inner.Item().Height(3);
            }

            if (!string.IsNullOrWhiteSpace(_profile.Email))
            {
                inner.Item().Text(_profile.Email.Trim())
                    .FontSize(DesignCStyles.SidebarBody)
                    .FontColor(DesignCStyles.SidebarText);
                inner.Item().Height(3);
            }

            if (!string.IsNullOrWhiteSpace(_profile.Location))
            {
                inner.Item().Text(_profile.Location.Trim())
                    .FontSize(DesignCStyles.SidebarBody)
                    .FontColor(DesignCStyles.SidebarText);
            }
        });

        col.Item().Height(8);

        var languages = ResolveLanguages();
        if (languages.Count > 0)
        {
            ComposeSidebarSectionLabel(col, "\u25C9", CvSectionTitleResolver.Languages(_data));
            col.Item().PaddingHorizontal(16).Column(inner =>
            {
                foreach (var lang in languages)
                {
                    inner.Item().Column(langBlock =>
                    {
                        langBlock.Spacing(2);
                        langBlock.Item().Row(r =>
                        {
                            r.RelativeItem()
                                .Text(lang.Name.ToUpperInvariant())
                                .FontSize(DesignCStyles.SmallText)
                                .FontColor(DesignCStyles.SidebarText)
                                .Bold();

                            r.ConstantItem(DesignCStyles.LanguageDotsColumnWidth).AlignRight().Row(dots =>
                            {
                                var filled = GetLanguageDotCount(lang.Level);
                                for (var i = 1; i <= 5; i++)
                                {
                                    var color = i <= filled ? DesignCStyles.DotFilled : DesignCStyles.DotEmpty;
                                    dots.ConstantItem(8).Text("\u25CF").FontSize(8).FontColor(color);
                                }
                            });
                        });

                        var levelLine = GetLevelLabel(lang.Level);
                        if (!string.IsNullOrWhiteSpace(levelLine))
                        {
                            langBlock.Item()
                                .Text(levelLine)
                                .FontSize(DesignCStyles.SmallText)
                                .FontColor(DesignCStyles.SidebarMuted)
                                .LineHeight(1.28f);
                        }
                    });
                    inner.Item().Height(5);
                }
            });
            col.Item().Height(8);
        }

        var hobbyLine = string.Join(" · ", (_data.Hobbies ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()));
        if (!string.IsNullOrWhiteSpace(hobbyLine))
        {
            ComposeSidebarSectionLabel(col, "\u2665", CvSectionTitleResolver.Interests(_data));
            col.Item().PaddingHorizontal(16)
                .Text(hobbyLine)
                .FontSize(DesignCStyles.SidebarBody)
                .FontColor(DesignCStyles.SidebarMuted)
                .LineHeight(1.35f);
            col.Item().Height(8);
        }
    }

    private void ComposePhotoWithAccent(ColumnDescriptor col)
    {
        col.Item().AlignCenter().Width(136).Height(150).Layers(layers =>
        {
            layers.Layer()
                .PaddingLeft(20)
                .PaddingTop(6)
                .Width(118)
                .Height(118)
                .Background(DesignCStyles.BlobColor);

            layers.Layer()
                .AlignLeft()
                .AlignBottom()
                .PaddingLeft(4)
                .PaddingBottom(6)
                .Text("\u25CF")
                .FontSize(24)
                .FontColor(DesignCStyles.BlobAccentDark);

            layers.PrimaryLayer().PaddingTop(10).PaddingLeft(5).Width(118).Height(118).Element(container =>
            {
                var framed = container.Border(1).BorderColor("#E5E7EB").Padding(2);
                if (_profileImageBytes is not null)
                {
                    framed.Image(_profileImageBytes).FitArea();
                }
                else
                {
                    framed.Background(DesignCStyles.BlobColor)
                        .AlignCenter()
                        .AlignMiddle()
                        .Text(GetInitials())
                        .FontSize(30)
                        .FontColor(DesignCStyles.BlobAccentDark)
                        .Bold();
                }
            });
        });
    }

    private static void ComposeSidebarSectionLabel(ColumnDescriptor col, string icon, string label)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(20).Height(20)
                .Background(DesignCStyles.IconBadgeBg)
                .AlignCenter()
                .AlignMiddle()
                .Text(icon)
                .FontSize(DesignCStyles.SidebarIconBadge)
                .FontColor(DesignCStyles.IconBadgeText)
                .Bold();
            r.ConstantItem(6);
            r.RelativeItem().AlignMiddle()
                .Text(label)
                .FontSize(DesignCStyles.SectionTitle)
                .FontColor(DesignCStyles.MainText)
                .Bold()
                .LetterSpacing(0.04f);
        });
        col.Item().Height(5);
    }

    private void ComposeMain(ColumnDescriptor col)
    {
        col.Item().PaddingVertical(16).PaddingHorizontal(16).Column(inner =>
        {
            var fullName = $"{_profile.FirstName} {_profile.LastName}".Trim().ToUpperInvariant();
            inner.Item().Text(fullName)
                .FontSize(DesignCStyles.NameSize)
                .FontColor(DesignCStyles.MainText)
                .Bold()
                .LetterSpacing(0.02f);

            inner.Item().Height(4);

            if (!string.IsNullOrWhiteSpace(_profile.Headline))
            {
                inner.Item()
                    .Background(DesignCStyles.CyanBadge)
                    .PaddingHorizontal(6)
                    .PaddingVertical(4)
                    .Text(_profile.Headline.Trim().ToUpperInvariant())
                    .FontSize(DesignCStyles.HeadlineSize)
                    .FontColor(DesignCStyles.CyanBadgeText)
                    .Bold()
                    .LetterSpacing(0.05f);
            }

            inner.Item().Height(5);

            var contactParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_profile.Email))
            {
                contactParts.Add(_profile.Email.Trim());
            }

            if (!string.IsNullOrWhiteSpace(_profile.Phone))
            {
                contactParts.Add(_profile.Phone.Trim());
            }

            if (!string.IsNullOrWhiteSpace(_profile.Location))
            {
                contactParts.Add(_profile.Location.Trim());
            }

            if (contactParts.Count > 0)
            {
                inner.Item().Text(string.Join("  |  ", contactParts))
                    .FontSize(DesignCStyles.HeaderContactSize)
                    .FontColor(DesignCStyles.MainMuted);
            }

            if (HasSocialLinks(_profile))
            {
                inner.Item().Height(3);
                inner.Item().Row(r =>
                {
                    if (!string.IsNullOrWhiteSpace(_profile.LinkedInUrl))
                    {
                        r.AutoItem()
                            .Background("#EFF6FF")
                            .PaddingHorizontal(3)
                            .PaddingVertical(2)
                            .Text($"in  {FormatUrl(_profile.LinkedInUrl)}")
                            .FontSize(DesignCStyles.SmallText)
                            .FontColor("#1D4ED8");
                        r.ConstantItem(6);
                    }

                    if (!string.IsNullOrWhiteSpace(_profile.GitHubUrl))
                    {
                        r.AutoItem()
                            .Background("#F9FAFB")
                            .PaddingHorizontal(3)
                            .PaddingVertical(2)
                            .Text($"gh  {FormatUrl(_profile.GitHubUrl)}")
                            .FontSize(DesignCStyles.SmallText)
                            .FontColor("#374151");
                        r.ConstantItem(6);
                    }

                    if (!string.IsNullOrWhiteSpace(_profile.PortfolioUrl))
                    {
                        r.AutoItem()
                            .Background("#F9FAFB")
                            .PaddingHorizontal(3)
                            .PaddingVertical(2)
                            .Text($"web  {FormatUrl(_profile.PortfolioUrl)}")
                            .FontSize(DesignCStyles.SmallText)
                            .FontColor("#374151");
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(_profile.WorkPermit))
            {
                inner.Item().Height(4);
                inner.Item()
                    .Background("#F0FDF4")
                    .Border(0.5f).BorderColor("#BBF7D0")
                    .PaddingHorizontal(4)
                    .PaddingVertical(3)
                    .Text($"✓  {_profile.WorkPermit.Trim()}")
                    .FontSize(DesignCStyles.SmallText)
                    .FontColor("#15803D");
            }

            inner.Item().Height(8);

            foreach (var sectionKey in CvStudioMainSectionOrder.Resolve(_data.ContentSectionOrder))
            {
                switch (sectionKey)
                {
                    case CvStudioMainSectionOrder.Summary:
                        if (!string.IsNullOrWhiteSpace(_profile.Summary))
                        {
                            ComposeMainSection(inner, "\u2712", CvSectionTitleResolver.QualificationsProfile(_data));
                            inner.Item()
                                .Text(_profile.Summary.Trim())
                                .FontSize(DesignCStyles.BodyText)
                                .FontColor(DesignCStyles.MainMuted)
                                .LineHeight(1.38f);
                            inner.Item().Height(5);
                        }
                        break;

                    case CvStudioMainSectionOrder.Work:
                        if (_workItems.Count > 0)
                        {
                            ComposeMainSection(inner, "\u2630", CvSectionTitleResolver.WorkExperience(_data));
                            foreach (var work in _workItems)
                                ComposeWorkItem(inner, work);
                            inner.Item().Height(3);
                        }
                        break;

                    case CvStudioMainSectionOrder.Education:
                        if (_educationItems.Count > 0)
                        {
                            ComposeMainSection(inner, "\u25CE", CvSectionTitleResolver.Education(_data));
                            foreach (var edu in _educationItems)
                                ComposeEduItem(inner, edu);
                            inner.Item().Height(3);
                        }
                        break;

                    case CvStudioMainSectionOrder.Skills:
                    {
                        var knowledgeGroups = GetKnowledgeGroupsForMainColumn();
                        if (knowledgeGroups.Count > 0)
                        {
                            ComposeMainSection(inner, "\u2605", CvSectionTitleResolver.Skills(_data));
                            ComposeKnowledgeGroupsMain(inner, knowledgeGroups);
                            inner.Item().Height(3);
                        }
                        break;
                    }

                    case CvStudioMainSectionOrder.Projects:
                        if (_projects.Count > 0)
                        {
                            ComposeMainSection(inner, "\u25C8", CvSectionTitleResolver.Projects(_data));
                            foreach (var project in _projects)
                                ComposeProjectItem(inner, project);
                        }
                        break;

                    case CvStudioMainSectionOrder.Languages:
                        break;
                }
            }
        });
    }

    private static bool IsLanguageSkillCategory(string? categoryName) =>
        (categoryName ?? string.Empty).Contains("sprach", StringComparison.OrdinalIgnoreCase);

    private static bool IsLinkSkillCategory(string? categoryName) =>
        (categoryName ?? string.Empty).Contains("link", StringComparison.OrdinalIgnoreCase);

    private List<SkillGroupData> GetKnowledgeGroupsForMainColumn()
    {
        return _skills
            .Where(g => !string.IsNullOrWhiteSpace(g.CategoryName))
            .Where(g => !IsLanguageSkillCategory(g.CategoryName))
            .Where(g => !IsLinkSkillCategory(g.CategoryName))
            .Where(g => (g.Items ?? []).Any(x => !string.IsNullOrWhiteSpace(x)))
            .ToList();
    }

    private static void ComposeKnowledgeGroupsMain(ColumnDescriptor col, List<SkillGroupData> groups)
    {
        foreach (var group in groups)
        {
            var items = string.Join(" · ", (group.Items ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()));
            if (string.IsNullOrWhiteSpace(items))
            {
                continue;
            }

            col.Item().Text(text =>
            {
                text.Span($"{group.CategoryName!.Trim()}: ").Bold().FontSize(DesignCStyles.BodyText);
                text.Span(items).FontSize(DesignCStyles.BodyText).FontColor(DesignCStyles.MainMuted);
            });
            col.Item().Height(DesignCStyles.ItemGap);
        }
    }

    private static void ComposeMainSection(ColumnDescriptor col, string icon, string title)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(20).Height(20)
                .Background(DesignCStyles.IconBadgeBg)
                .AlignCenter()
                .AlignMiddle()
                .Text(icon)
                .FontSize(DesignCStyles.SectionIconSize)
                .FontColor(DesignCStyles.IconBadgeText)
                .Bold();
            r.ConstantItem(8);
            r.RelativeItem()
                .Text(title)
                .FontSize(DesignCStyles.SectionTitle)
                .FontColor(DesignCStyles.MainText)
                .Bold()
                .LetterSpacing(0.06f);
        });
        col.Item().Height(2).BorderBottom(0.5f).BorderColor(DesignCStyles.SectionLine);
        col.Item().Height(4);
    }

    private static void ComposeWorkItem(ColumnDescriptor col, WorkItemData work)
    {
        var (company, location) = SplitByPipe(work.Company);

        col.Item().Row(r =>
        {
            r.RelativeItem()
                .Text(company)
                .FontSize(DesignCStyles.BodyText)
                .FontColor(DesignCStyles.MainText)
                .Bold();
            r.ConstantItem(70).AlignRight()
                .Text(location ?? string.Empty)
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.MainMuted);
        });

        col.Item().Row(r =>
        {
            r.RelativeItem()
                .Text(work.Role ?? string.Empty)
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.MainMuted);
            r.ConstantItem(90).AlignRight()
                .Text(BuildDateRange(work.StartDate, work.EndDate))
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.MainMuted);
        });

        col.Item().Height(2);

        var bullets = work.Bullets ?? [];
        if (bullets.Count > 0)
        {
            foreach (var bullet in bullets.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                col.Item().Row(r =>
                {
                    r.ConstantItem(DesignCStyles.BulletIndent)
                        .Text("\u2022")
                        .FontSize(DesignCStyles.BodyText)
                        .FontColor(DesignCStyles.BulletColor);
                    r.RelativeItem()
                        .Text(bullet.Trim())
                        .FontSize(DesignCStyles.BodyText)
                        .FontColor(DesignCStyles.MainText)
                        .LineHeight(1.34f);
                });
                col.Item().Height(1);
            }
        }
        else if (!string.IsNullOrWhiteSpace(work.Description))
        {
            col.Item().Row(r =>
            {
                r.ConstantItem(DesignCStyles.BulletIndent)
                    .Text("\u2022")
                    .FontColor(DesignCStyles.BulletColor);
                r.RelativeItem()
                    .Text(work.Description.Trim())
                    .FontSize(DesignCStyles.BodyText)
                    .LineHeight(1.35f);
            });
        }

        col.Item().Height(DesignCStyles.ItemGap);
    }

    private static void ComposeEduItem(ColumnDescriptor col, EducationItemData edu)
    {
        var (school, location) = SplitByPipe(edu.School);

        col.Item().Row(r =>
        {
            r.RelativeItem()
                .Text(school)
                .FontSize(DesignCStyles.BodyText)
                .Bold();
            r.ConstantItem(70).AlignRight()
                .Text(location ?? string.Empty)
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.MainMuted);
        });

        col.Item().Row(r =>
        {
            r.RelativeItem()
                .Text(edu.Degree ?? string.Empty)
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.MainMuted);
            r.ConstantItem(90).AlignRight()
                .Text(BuildDateRange(edu.StartDate, edu.EndDate))
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.MainMuted);
        });

        if (!string.IsNullOrWhiteSpace(edu.Description))
        {
            col.Item().Row(r =>
            {
                r.ConstantItem(DesignCStyles.BulletIndent)
                    .Text("\u2022")
                    .FontColor(DesignCStyles.BulletColor);
                r.RelativeItem()
                    .Text(edu.Description.Trim())
                    .FontSize(DesignCStyles.BodyText)
                    .LineHeight(1.35f)
                    .FontColor(DesignCStyles.MainMuted);
            });
        }

        col.Item().Height(DesignCStyles.ItemGap);
    }

    private static void ComposeProjectItem(ColumnDescriptor col, ResumeProjectItem project)
    {
        col.Item().Text(project.Name ?? string.Empty)
            .FontSize(DesignCStyles.BodyText)
            .Bold();

        if (!string.IsNullOrWhiteSpace(project.Description))
        {
            col.Item().Row(r =>
            {
                r.ConstantItem(DesignCStyles.BulletIndent)
                    .Text("\u2022")
                    .FontColor(DesignCStyles.BulletColor);
                r.RelativeItem()
                    .Text(project.Description.Trim())
                    .FontSize(DesignCStyles.BodyText)
                    .FontColor(DesignCStyles.MainText)
                    .LineHeight(1.35f);
            });
        }

        var technologies = project.Technologies ?? [];
        if (technologies.Count > 0)
        {
            var tech = technologies.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim());
            col.Item().PaddingLeft(DesignCStyles.BulletIndent)
                .Text(string.Join(" \u00B7 ", tech))
                .FontSize(DesignCStyles.SmallText)
                .FontColor(DesignCStyles.CyanDark);
        }

        col.Item().Height(DesignCStyles.ItemGap);
    }

    private List<(string Name, string? Level)> ResolveLanguages()
    {
        var languageGroup = _skills.FirstOrDefault(s =>
            (s.CategoryName ?? string.Empty).Contains("sprach", StringComparison.OrdinalIgnoreCase));

        var languageItems = languageGroup?.Items ?? [];
        if (languageItems.Count == 0)
            return [];

        return languageItems
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(ParseLanguage)
            .ToList();
    }

    private static (string Name, string? Level) ParseLanguage(string raw)
    {
        var value = raw.Trim();
        var open = value.IndexOf('(');
        var close = value.IndexOf(')');
        if (open > 0 && close > open)
        {
            var name = value[..open].Trim();
            var level = value[(open + 1)..close].Trim();
            return (name, level);
        }

        var parts = value.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var last = parts[^1];
            if (last.Length <= 3 || last.Contains('-'))
                return (string.Join(" ", parts[..^1]), last);
        }

        return (value, null);
    }

    private string GetInitials()
    {
        var first = (_profile.FirstName ?? string.Empty).FirstOrDefault();
        var last = (_profile.LastName ?? string.Empty).FirstOrDefault();
        return $"{first}{last}".ToUpperInvariant();
    }

    private static string GetLevelLabel(string? level)
    {
        return level?.ToUpperInvariant() switch
        {
            "C2" or "MUTTERSPRACHE" or "NATIVE" => "Muttersprache",
            "C1" => "Advanced",
            "B2" => "Gut",
            "B1" => "Mittel",
            "A2" or "A1" => "Basic",
            "ADVANCED" => "Advanced",
            "INTERMEDIATE" => "Gut",
            "BASIC" => "Basic",
            _ => level ?? string.Empty
        };
    }

    private static int GetLanguageDotCount(string? level)
    {
        return level?.ToLowerInvariant() switch
        {
            "c2" or "muttersprache" or "native" => 5,
            "c1" or "advanced" => 4,
            "b2" or "intermediate" => 3,
            "b1" => 2,
            "a2" or "a1" or "basic" => 1,
            _ => 3
        };
    }

    private static bool HasSocialLinks(ProfileData profile)
    {
        return !string.IsNullOrWhiteSpace(profile.LinkedInUrl)
            || !string.IsNullOrWhiteSpace(profile.GitHubUrl)
            || !string.IsNullOrWhiteSpace(profile.PortfolioUrl);
    }

    private static string FormatUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        return url
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/')
            .Trim();
    }

    private static (string Name, string? Location) SplitByPipe(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string.Empty, null);

        var parts = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (raw.Trim(), null),
            1 => (parts[0], null),
            _ => (parts[0], parts[1])
        };
    }

    private static string BuildDateRange(string startDate, string endDate)
    {
        var normalizedStart = NormalizeDate(startDate);
        var normalizedEnd = NormalizeDate(endDate);

        if (string.IsNullOrWhiteSpace(normalizedStart) && string.IsNullOrWhiteSpace(normalizedEnd))
            return string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEnd))
            return normalizedStart;
        if (string.IsNullOrWhiteSpace(normalizedStart))
            return normalizedEnd;

        return $"{normalizedStart} \u2013 {normalizedEnd}";
    }

    private static string NormalizeDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var value = raw.Trim();
        if (value.Equals("present", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("current", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("heute", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("aktuell", StringComparison.OrdinalIgnoreCase))
        {
            return "Heute";
        }

        var formats = new[] { "MM/yyyy", "M/yyyy", "MM.yyyy", "M.yyyy", "yyyy-MM", "yyyy" };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("MM/yyyy", CultureInfo.InvariantCulture);

        return value;
    }
}

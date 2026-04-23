using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CvStudio.Application;
using CvStudio.Application.Contracts;
using CvStudio.Application.Services;
using CvStudio.Infrastructure.Pdf.Designs.DesignC;
using System.Diagnostics;

namespace CvStudio.Infrastructure.Pdf;

public sealed class QuestPdfGenerator : IPdfGenerator
{
    private const string DefaultGithubUrl = "https://github.com/max-mustermann-dev";
    private const string DefaultPdfFontFamily = "Lato";

    private static readonly HttpClient ImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static class Style
    {
        public const float BaseFont = 10.5f;
        public const float NameFont = 22f;
        public const float RoleFont = 12f;
        public const float SectionFont = 11f;
        public static readonly string Navy = "#1A3A5C";
        public static readonly string Teal = "#1A7A6E";
        public static readonly string Muted = "#7D8A99";
        public static readonly string Body = "#1C2833";
        public static readonly string SectionBg = "#F2F6FA";
        public static readonly string Rule = "#C5D5E8";
    }

    private static class IconUrls
    {
        public const string Email = "https://img.icons8.com/ios-filled/50/1A7A6E/new-post.png";
        public const string Phone = "https://img.icons8.com/ios-filled/50/2563EB/phone.png";
        public const string Location = "https://img.icons8.com/ios-filled/50/F59E0B/marker.png";
        public const string LinkedIn = "https://img.icons8.com/ios-filled/50/0A66C2/linkedin.png";
        public const string Github = "https://img.icons8.com/ios-glyphs/50/374151/github.png";
    }

    private enum ContactIconKind
    {
        Email,
        Phone,
        Location,
        LinkedIn,
        Github
    }

    private sealed record ContactEntry(ContactIconKind IconKind, string Text);

    public byte[] GenerateFromResumeJson(string resumeJson, PdfDesign design = PdfDesign.DesignA)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var data = CvStudioMapper.Deserialize(resumeJson);
        var profileImageBytes = LoadProfileImageBytes(data.Profile.ProfileImageUrl);

        return design switch
        {
            PdfDesign.DesignB => GenerateDesignB(data, profileImageBytes),
            PdfDesign.DesignC => GenerateDesignC(data, profileImageBytes),
            _ => GenerateDesignA(data, profileImageBytes)
        };
    }

    private static byte[] GenerateDesignA(ResumeData data, byte[]? profileImageBytes)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x
                    .FontFamily(DefaultPdfFontFamily)
                    .FontSize(Style.BaseFont)
                    .FontColor(Style.Body)
                    .LineHeight(1.2f));

                page.Content().Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Element(c => RenderHeader(c, data, profileImageBytes));
                    column.Item().LineHorizontal(1.2f).LineColor(Style.Navy);

                    if (!string.IsNullOrWhiteSpace(data.Profile.Summary))
                    {
                        column.Item().Element(c => RenderSection(c, CvSectionTitleResolver.QualificationsProfile(data), section =>
                        {
                            section.Item().Text(data.Profile.Summary.Trim());
                        }));
                    }

                    column.Item().Element(c => RenderSkillsAndKnowledge(c, data));

                    if (data.WorkItems.Count > 0)
                    {
                        column.Item().Element(c => RenderSection(c, CvSectionTitleResolver.WorkExperience(data), section =>
                        {
                            foreach (var work in data.WorkItems)
                            {
                                section.Item().Element(x => RenderWorkEntry(x, work));
                                section.Item().PaddingBottom(2);
                            }
                        }));
                    }

                    if (data.EducationItems.Count > 0)
                    {
                        column.Item().Element(c => RenderSection(c, CvSectionTitleResolver.Education(data), section =>
                        {
                            foreach (var education in data.EducationItems)
                            {
                                section.Item().Element(x => RenderEducationEntry(x, education));
                                section.Item().PaddingBottom(2);
                            }
                        }));
                    }

                    column.Item().Element(c => RenderLanguagesAndInterests(c, data));
                });
            });
        }).GeneratePdf();
    }

    private static byte[] GenerateDesignB(ResumeData data, byte[]? profileImageBytes)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x
                    .FontFamily(DefaultPdfFontFamily)
                    .FontSize(10.8f)
                    .FontColor("#222222")
                    .LineHeight(1.22f));

                page.Content().Column(column =>
                {
                    column.Spacing(7);
                    column.Item().Element(c => RenderDesignBHeader(c, data, profileImageBytes));

                    if (!string.IsNullOrWhiteSpace(data.Profile.Summary))
                    {
                        column.Item().Text(data.Profile.Summary.Trim()).FontSize(11f);
                    }

                    if (data.WorkItems.Count > 0)
                    {
                        column.Item().Element(c => RenderDesignBSection(c, CvSectionTitleResolver.WorkExperience(data), section =>
                        {
                            foreach (var work in data.WorkItems)
                            {
                                section.Item().Element(x => RenderDesignBWorkEntry(x, work));
                                section.Item().PaddingBottom(3);
                            }
                        }));
                    }

                    if (data.EducationItems.Count > 0)
                    {
                        column.Item().Element(c => RenderDesignBSection(c, CvSectionTitleResolver.Education(data), section =>
                        {
                            foreach (var education in data.EducationItems)
                            {
                                section.Item().Element(x => RenderDesignBEducationEntry(x, education));
                                section.Item().PaddingBottom(3);
                            }
                        }));
                    }

                    var designBKnowledgeGroups = (data.Skills ?? [])
                        .Where(s =>
                            !string.IsNullOrWhiteSpace(s.CategoryName) &&
                            !IsLanguageCategory(s.CategoryName) &&
                            !IsLinkCategory(s.CategoryName))
                        .Select(s => new
                        {
                            s.CategoryName,
                            Line = string.Join(" · ", (s.Items ?? []).Where(static x => !string.IsNullOrWhiteSpace(x)))
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.Line))
                        .ToList();

                    if (designBKnowledgeGroups.Count > 0)
                    {
                        column.Item().Element(c => RenderDesignBSection(c, CvSectionTitleResolver.Skills(data), section =>
                        {
                            foreach (var row in designBKnowledgeGroups)
                            {
                                section.Item().Text(text =>
                                {
                                    text.Span($"{row.CategoryName.Trim()}: ").Bold().FontSize(10.5f);
                                    text.Span(row.Line).FontSize(10.5f);
                                });
                            }
                        }));
                    }

                    column.Item().Element(c => RenderDesignBLanguagesAndInterests(c, data));
                });
            });
        }).GeneratePdf();
    }

    private static byte[] GenerateDesignC(ResumeData data, byte[]? profileImageBytes)
    {
        return new DesignCDocument(data, profileImageBytes).GeneratePdf();
    }

    private static void RenderHeader(IContainer container, ResumeData data, byte[]? profileImageBytes)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(82).Height(82).Element(img =>
                {
                    if (profileImageBytes is null)
                    {
                        img.Border(0.8f).BorderColor(Style.Rule).Background(Colors.Grey.Lighten3);
                    }
                    else
                    {
                        img.Image(profileImageBytes).FitArea();
                    }
                });

                row.ConstantItem(10);

                row.RelativeItem().BorderLeft(1.2f).BorderColor(Style.Navy).PaddingLeft(10).Column(inner =>
                {
                    inner.Spacing(2);

                    inner.Item().Text($"{data.Profile.FirstName} {data.Profile.LastName}".Trim())
                        .FontColor("#111827")
                        .FontSize(26f)
                        .Bold()
                        .LetterSpacing(-0.01f);

                    if (!string.IsNullOrWhiteSpace(data.Profile.Headline))
                    {
                        inner.Item().Text(data.Profile.Headline.Trim())
                            .FontColor("#6B7280")
                            .FontSize(11f)
                            .LetterSpacing(0.01f);
                    }
                });
            });

            col.Item().Height(6);

            var contactParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(data.Profile.Email))
            {
                contactParts.Add(data.Profile.Email.Trim());
            }

            if (!string.IsNullOrWhiteSpace(data.Profile.Phone))
            {
                contactParts.Add(data.Profile.Phone.Trim());
            }

            if (!string.IsNullOrWhiteSpace(data.Profile.Location))
            {
                contactParts.Add(data.Profile.Location.Trim());
            }

            if (contactParts.Count > 0)
            {
                col.Item().Text(string.Join("  |  ", contactParts))
                    .FontSize(8.5f)
                    .FontColor("#374151");
            }

            if (HasSocialLinks(data.Profile))
            {
                col.Item().Height(3);
                var links = new List<string>();
                if (!string.IsNullOrWhiteSpace(data.Profile.LinkedInUrl))
                {
                    links.Add($"in: {FormatUrl(data.Profile.LinkedInUrl)}");
                }

                if (!string.IsNullOrWhiteSpace(data.Profile.GitHubUrl))
                {
                    links.Add($"gh: {FormatUrl(data.Profile.GitHubUrl)}");
                }

                if (!string.IsNullOrWhiteSpace(data.Profile.PortfolioUrl))
                {
                    links.Add($"web: {FormatUrl(data.Profile.PortfolioUrl)}");
                }

                col.Item().Text(string.Join("   ", links))
                    .FontSize(8f)
                    .FontColor("#6B7280");
            }

            if (!string.IsNullOrWhiteSpace(data.Profile.WorkPermit))
            {
                col.Item().Height(4);
                col.Item().AlignRight()
                    .Background("#F0FDF4")
                    .PaddingHorizontal(3)
                    .PaddingVertical(2)
                    .Text($"✓ {data.Profile.WorkPermit.Trim()}")
                    .FontSize(7.5f)
                    .FontColor("#15803D");
            }
        });
    }

    private static void RenderDesignBHeader(IContainer container, ResumeData data, byte[]? profileImageBytes)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Row(row =>
            {
                const float photoSize = 92f;
                row.ConstantItem(photoSize).Height(photoSize).Element(img =>
                {
                    if (profileImageBytes is null)
                    {
                        img.Border(0.8f).BorderColor("#D5D7DC").Background("#F1F2F6");
                    }
                    else
                    {
                        img.Image(profileImageBytes).FitArea();
                    }
                });

                row.ConstantItem(14);
                row.RelativeItem().AlignMiddle().Column(right =>
                {
                    right.Spacing(1);
                    right.Item().Text($"{data.Profile.FirstName} {data.Profile.LastName}".Trim()).FontSize(25f).Bold();

                    if (!string.IsNullOrWhiteSpace(data.Profile.Headline))
                    {
                        right.Item().Text(data.Profile.Headline.Trim()).FontSize(14f).FontColor("#5F6C7A");
                    }
                });
            });

            column.Item().PaddingTop(2).Column(contactCol =>
            {
                var contacts = new List<string>();
                if (!string.IsNullOrWhiteSpace(data.Profile.Email))
                {
                    contacts.Add(data.Profile.Email.Trim());
                }

                if (!string.IsNullOrWhiteSpace(data.Profile.Phone))
                {
                    contacts.Add(data.Profile.Phone.Trim());
                }

                if (!string.IsNullOrWhiteSpace(data.Profile.Location))
                {
                    contacts.Add(data.Profile.Location.Trim());
                }

                if (contacts.Count > 0)
                {
                    contactCol.Item().Text(string.Join("  |  ", contacts))
                        .FontSize(8f)
                        .FontColor("#6B7280");
                }

                if (HasSocialLinks(data.Profile))
                {
                    contactCol.Item().Height(2);
                    var links = new List<string>();
                    if (!string.IsNullOrWhiteSpace(data.Profile.LinkedInUrl))
                    {
                        links.Add($"LinkedIn: {FormatUrl(data.Profile.LinkedInUrl)}");
                    }

                    if (!string.IsNullOrWhiteSpace(data.Profile.GitHubUrl))
                    {
                        links.Add($"GitHub: {FormatUrl(data.Profile.GitHubUrl)}");
                    }

                    if (!string.IsNullOrWhiteSpace(data.Profile.PortfolioUrl))
                    {
                        links.Add($"Portfolio: {FormatUrl(data.Profile.PortfolioUrl)}");
                    }

                    contactCol.Item().Text(string.Join("   ", links))
                        .FontSize(7.5f)
                        .FontColor("#6B7280");
                }

                if (!string.IsNullOrWhiteSpace(data.Profile.WorkPermit))
                {
                    contactCol.Item().Height(4);
                    contactCol.Item()
                        .Background("#F0FDF4")
                        .PaddingHorizontal(3)
                        .PaddingVertical(2)
                        .Text($"✓ {data.Profile.WorkPermit.Trim()}")
                        .FontSize(7.5f)
                        .FontColor("#15803D");
                }
            });
        });
    }

    private static void RenderDesignBSection(IContainer container, string title, Action<ColumnDescriptor> renderContent)
    {
        container.Column(column =>
        {
            column.Spacing(3);
            column.Item().PaddingTop(2).Text(title).FontSize(14f).Bold();
            column.Item().LineHorizontal(0.8f).LineColor("#2C2F34");
            renderContent(column);
        });
    }

    private static void RenderDesignBWorkEntry(IContainer container, WorkItemData item)
    {
        var (company, location) = SplitByPipe(item.Company);
        var role = string.IsNullOrWhiteSpace(item.Role) ? string.Empty : item.Role.Trim().ToUpperInvariant();
        var organization = string.IsNullOrWhiteSpace(company) ? string.Empty : company.Trim().ToUpperInvariant();

        container.Column(column =>
        {
            column.Spacing(1);
            if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(organization))
            {
                var title = string.IsNullOrWhiteSpace(role)
                    ? organization
                    : string.IsNullOrWhiteSpace(organization)
                        ? role
                        : $"{role} - {organization}";

                if (!string.IsNullOrWhiteSpace(location))
                {
                    title += $" | {location.Trim()}";
                }

                column.Item().Text(title).FontSize(11.6f).Bold();
            }

            var dateRange = BuildDateRange(item.StartDate, item.EndDate);
            if (!string.IsNullOrWhiteSpace(dateRange))
            {
                column.Item().Text(dateRange).FontSize(10.2f).FontColor("#7D8A99");
            }

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                column.Item().Text(item.Description.Trim()).FontSize(10.6f).FontColor("#3E4652");
            }

            foreach (var bullet in item.Bullets.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                column.Item().PaddingLeft(12).Text($"• {bullet.Trim()}").FontSize(10.9f);
            }
        });
    }

    private static void RenderDesignBEducationEntry(IContainer container, EducationItemData item)
    {
        var (school, location) = SplitByPipe(item.School);
        var schoolLine = string.IsNullOrWhiteSpace(location) ? school : $"{school} | {location}";

        container.Column(column =>
        {
            column.Spacing(1);
            if (!string.IsNullOrWhiteSpace(item.Degree))
            {
                column.Item().Text(item.Degree.Trim()).Bold().FontSize(11.1f);
            }

            if (!string.IsNullOrWhiteSpace(schoolLine))
            {
                column.Item().Text(schoolLine).FontSize(10.8f);
            }

            var range = BuildDateRange(item.StartDate, item.EndDate);
            if (!string.IsNullOrWhiteSpace(range))
            {
                column.Item().Text(range).FontSize(10.2f).FontColor("#7D8A99");
            }
        });
    }

    private static void RenderSection(IContainer container, string title, Action<ColumnDescriptor> renderContent)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Background(Style.SectionBg).BorderLeft(2f).BorderColor(Style.Navy).PaddingLeft(6).PaddingVertical(2)
                .Text(title)
                .FontColor(Style.Navy)
                .FontSize(Style.SectionFont)
                .Bold();
            renderContent(column);
        });
    }

    private static void RenderSkillsAndKnowledge(IContainer container, ResumeData data)
    {
        var visibleGroups = data.Skills
            .Where(g => !string.IsNullOrWhiteSpace(g.CategoryName) && !IsLanguageCategory(g.CategoryName) && !IsLinkCategory(g.CategoryName))
            .Select(g => new { Group = g, Items = JoinItemsWithDot(g.Items) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Items))
            .ToList();

        if (visibleGroups.Count == 0)
        {
            return;
        }

        RenderSection(container, CvSectionTitleResolver.Skills(data), section =>
        {
            section.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(140);
                    columns.RelativeColumn();
                });

                for (var i = 0; i < visibleGroups.Count; i++)
                {
                    var row = visibleGroups[i];
                    var borderWidth = i < visibleGroups.Count - 1 ? 0.8f : 0f;

                    table.Cell().BorderBottom(borderWidth).BorderColor(Style.Rule).PaddingVertical(4)
                        .Text(row.Group.CategoryName.Trim())
                        .Bold()
                        .FontColor(Style.Navy);

                    table.Cell().BorderBottom(borderWidth).BorderColor(Style.Rule).PaddingVertical(4)
                        .Text(row.Items)
                        .FontColor(Style.Muted);
                }
            });
        });
    }

    private static void RenderWorkEntry(IContainer container, WorkItemData item)
    {
        var (company, location) = SplitByPipe(item.Company);

        container.Column(column =>
        {
            column.Spacing(1);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(company).Bold().FontColor(Style.Navy);
                row.ConstantItem(125).AlignRight().Text(BuildDateRange(item.StartDate, item.EndDate)).FontColor(Style.Muted).Italic();
            });

            if (!string.IsNullOrWhiteSpace(location) || !string.IsNullOrWhiteSpace(item.Role))
            {
                column.Item().Text(text =>
                {
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        text.Span(location.Trim()).FontSize(9.2f).FontColor(Style.Muted);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Role))
                    {
                        if (!string.IsNullOrWhiteSpace(location))
                        {
                            text.Span("   ");
                        }

                        text.Span(item.Role.Trim()).FontColor(Style.Teal).Bold().Italic();
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                column.Item().PaddingTop(1).Text(item.Description.Trim()).FontColor(Style.Muted).Italic();
            }

            foreach (var bullet in item.Bullets.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                column.Item().PaddingLeft(8).Text($"• {bullet.Trim()}").FontColor(Style.Body);
            }
        });
    }

    private static void RenderEducationEntry(IContainer container, EducationItemData item)
    {
        var (school, location) = SplitByPipe(item.School);

        container.Column(column =>
        {
            column.Spacing(1);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(school).Bold();
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        text.Span(" | ").FontColor(Style.Muted);
                        text.Span(location).FontColor(Style.Muted);
                    }
                });
                row.ConstantItem(125).AlignRight().Text(BuildDateRange(item.StartDate, item.EndDate)).FontColor(Style.Muted).Italic();
            });

            if (!string.IsNullOrWhiteSpace(item.Degree))
            {
                column.Item().Text(item.Degree).FontColor(Style.Teal).Italic().SemiBold();
            }
        });
    }

    private static void RenderLanguagesAndInterests(IContainer container, ResumeData data)
    {
        if (!TryBuildLanguagesInterestsLines(data, out var languageLine, out var hobbies))
        {
            return;
        }

        RenderSection(container, CvSectionTitleResolver.LanguagesAndInterests(data), section =>
        {
            section.Item().Text(text => AppendLanguagesInterestsSpans(text, languageLine, hobbies, data));
        });
    }

    private static void RenderDesignBLanguagesAndInterests(IContainer container, ResumeData data)
    {
        var languageItems = (data.Skills ?? [])
            .Where(g => IsLanguageCategory(g.CategoryName))
            .SelectMany(g => g.Items ?? [])
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .ToList();

        var hobbyItems = (data.Hobbies ?? [])
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .ToList();

        if (languageItems.Count == 0 && hobbyItems.Count == 0)
        {
            return;
        }

        RenderDesignBSection(container, CvSectionTitleResolver.LanguagesAndInterests(data), section =>
        {
            if (languageItems.Count > 0)
            {
                section.Item()
                    .PaddingBottom(hobbyItems.Count > 0 ? 4 : 0)
                    .Text(text =>
                    {
                        text.Span(CvSectionTitleResolver.DesignBLanguagesRowLabel(data)).Bold().FontSize(10.5f);
                        text.Span(string.Join(" · ", languageItems)).FontSize(10.5f);
                    });
            }

            if (hobbyItems.Count > 0)
            {
                section.Item().Text(text =>
                {
                    text.Span(CvSectionTitleResolver.DesignBInterestsRowLabel(data)).Bold().FontSize(10.5f);
                    text.Span(string.Join(" · ", hobbyItems)).FontSize(10.5f);
                });
            }
        });
    }

    private static bool TryBuildLanguagesInterestsLines(ResumeData data, out string languageLine, out string hobbies)
    {
        languageLine = string.Join(" - ", (data.Skills ?? [])
            .Where(g => IsLanguageCategory(g.CategoryName))
            .SelectMany(g => g.Items ?? [])
            .Where(static x => !string.IsNullOrWhiteSpace(x)));

        hobbies = string.Join(" - ", (data.Hobbies ?? []).Where(static x => !string.IsNullOrWhiteSpace(x)));

        return !string.IsNullOrWhiteSpace(languageLine) || !string.IsNullOrWhiteSpace(hobbies);
    }

    private static void AppendLanguagesInterestsSpans(TextDescriptor text, string languageLine, string hobbies, ResumeData data)
    {
        if (!string.IsNullOrWhiteSpace(languageLine))
        {
            text.Span(CvSectionTitleResolver.LanguagesInlineLabel(data)).Bold().FontColor(Style.Navy);
            text.Span(languageLine);
        }

        if (!string.IsNullOrWhiteSpace(languageLine) && !string.IsNullOrWhiteSpace(hobbies))
        {
            text.Span("    |    ").FontColor(Style.Muted);
        }

        if (!string.IsNullOrWhiteSpace(hobbies))
        {
            text.Span(CvSectionTitleResolver.InterestsInlineLabel(data)).Bold().FontColor(Style.Navy);
            text.Span(hobbies);
        }
    }

    private static byte[]? LoadProfileImageBytes(string? imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl) &&
            Uri.TryCreate(imageUrl.Trim(), UriKind.Absolute, out var uri))
        {
            try
            {
                var bytes = ImageHttpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                if (bytes.Length > 0)
                {
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                LogSuppressedException(ex, "Remote profile image fetch failed.");
            }
        }

        try
        {
            var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "profile-placeholder.png");
            if (File.Exists(localPath))
            {
                var bytes = File.ReadAllBytes(localPath);
                if (bytes.Length > 0)
                {
                    return bytes;
                }
            }
        }
        catch (Exception ex)
        {
            LogSuppressedException(ex, "Local profile image fallback failed.");
            // ignore local fallback errors
        }

        return null;
    }

    private static List<ContactEntry> BuildContacts(ResumeData data)
    {
        var contacts = new List<ContactEntry>();

        if (!string.IsNullOrWhiteSpace(data.Profile.Email))
        {
            contacts.Add(new ContactEntry(ContactIconKind.Email, data.Profile.Email.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(data.Profile.Phone))
        {
            contacts.Add(new ContactEntry(ContactIconKind.Phone, data.Profile.Phone.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(data.Profile.Location))
        {
            contacts.Add(new ContactEntry(ContactIconKind.Location, data.Profile.Location.Trim()));
        }

        var linkedIn = data.Skills
            .SelectMany(g => g.Items)
            .FirstOrDefault(i => i.Contains("linkedin", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(linkedIn))
        {
            contacts.Add(new ContactEntry(ContactIconKind.LinkedIn, linkedIn.Trim()));
        }

        var github = data.Skills
            .SelectMany(g => g.Items)
            .FirstOrDefault(i => i.Contains("github.com", StringComparison.OrdinalIgnoreCase));
        github ??= DefaultGithubUrl;
        contacts.Add(new ContactEntry(ContactIconKind.Github, github.Trim()));

        return contacts;
    }

    private static void RenderContactLine(IContainer container, IReadOnlyList<ContactEntry> entries)
    {
        container.Row(row =>
        {
            foreach (var entry in entries)
            {
                row.RelativeItem().Row(item =>
                {
                    item.ConstantItem(11).Height(11).Element(icon =>
                    {
                        var iconBytes = LoadIconBytes(entry.IconKind);
                        if (iconBytes is not null)
                        {
                            icon.Image(iconBytes).FitArea();
                        }
                    });
                    item.ConstantItem(3);
                    item.RelativeItem().Text(entry.Text).FontSize(9.2f).FontColor(Style.Body);
                });
            }
        });
    }

    private static byte[]? LoadIconBytes(ContactIconKind iconKind)
    {
        var url = iconKind switch
        {
            ContactIconKind.Email => IconUrls.Email,
            ContactIconKind.Phone => IconUrls.Phone,
            ContactIconKind.Location => IconUrls.Location,
            ContactIconKind.LinkedIn => IconUrls.LinkedIn,
            ContactIconKind.Github => IconUrls.Github,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            return ImageHttpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogSuppressedException(ex, "Contact icon fetch failed.");
            return null;
        }
    }

    private static void LogSuppressedException(Exception exception, string context)
    {
        Trace.TraceWarning("{0} {1}: {2}", nameof(QuestPdfGenerator), context, exception.Message);
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
        {
            return string.Empty;
        }

        return url
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/')
            .Trim();
    }

    private static (string Name, string? Location) SplitByPipe(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, null);
        }

        var parts = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (raw.Trim(), null),
            1 => (parts[0], null),
            _ => (parts[0], parts[1])
        };
    }

    private static bool IsLanguageCategory(string categoryName)
    {
        return categoryName.Contains("sprach", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLinkCategory(string categoryName)
    {
        return categoryName.Contains("link", StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinItemsWithDot(IEnumerable<string> items)
    {
        return string.Join(" · ", items.Where(static x => !string.IsNullOrWhiteSpace(x)).Select(static x => x.Trim()));
    }

    private static string BuildDateRange(string startDate, string endDate)
    {
        var normalizedStart = NormalizeDate(startDate);
        var normalizedEnd = NormalizeDate(endDate);

        if (string.IsNullOrWhiteSpace(normalizedStart) && string.IsNullOrWhiteSpace(normalizedEnd))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(normalizedEnd))
        {
            return normalizedStart;
        }

        if (string.IsNullOrWhiteSpace(normalizedStart))
        {
            return normalizedEnd;
        }

        return $"{normalizedStart} - {normalizedEnd}";
    }

    private static string NormalizeDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();

        if (value.Equals("present", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("current", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("heute", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("aktuell", StringComparison.OrdinalIgnoreCase))
        {
            return "Heute";
        }

        var formats = new[] { "MM/yyyy", "M/yyyy", "MM.yyyy", "M.yyyy", "yyyy-MM", "yyyy" };
        if (DateTime.TryParseExact(value, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }
}

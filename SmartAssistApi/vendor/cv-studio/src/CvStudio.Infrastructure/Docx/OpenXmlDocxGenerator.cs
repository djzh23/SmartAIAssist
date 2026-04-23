using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CvStudio.Application;
using CvStudio.Application.Contracts;
using CvStudio.Application.Services;
using System.Diagnostics;
using System.Threading;

namespace CvStudio.Infrastructure.Docx;

public sealed class OpenXmlDocxGenerator : IDocxGenerator
{
    private const string DefaultGithubUrl = "https://github.com/max-mustermann-dev";

    private static readonly HttpClient ImageHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private const string Navy = "1A3A5C";
    private const string Teal = "1A7A6E";
    private const string Muted = "7D8A99";
    private const string BodyColor = "1C2833";
    private const string SectionBg = "F2F6FA";
    private const string Rule = "C5D5E8";
    private static int _docPropertyCounter = 100;

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

    public byte[] GenerateFromResumeJson(string resumeJson)
    {
        var data = CvStudioMapper.Deserialize(resumeJson);
        var profileImageBytes = LoadProfileImageBytes(data.Profile.ProfileImageUrl);

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            ConfigureDocument(mainPart);

            var body = mainPart.Document.Body!;
            body.Append(CreateHeaderTable(mainPart, data, profileImageBytes));
            body.Append(CreateRuleParagraph(Navy, 10U));
            AppendOrderedMainSections(body, data);

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void ConfigureDocument(MainDocumentPart mainPart)
    {
        var sectionProperties = new SectionProperties(
            new PageSize { Width = 11906U, Height = 16838U },
            new PageMargin
            {
                Top = 1247,
                Right = 1247U,
                Bottom = 1247,
                Left = 1247U,
                Header = 720U,
                Footer = 720U,
                Gutter = 0U
            });

        mainPart.Document.Body ??= new Body();
        mainPart.Document.Body.Append(sectionProperties);
    }

    private static Table CreateHeaderTable(MainDocumentPart mainPart, ResumeData data, byte[]? profileImageBytes)
    {
        var contacts = BuildContacts(data);
        var firstLine = contacts.Take(3).ToList();
        var secondLine = contacts.Skip(3).Take(2).ToList();

        var table = new Table(
            new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableBorders(
                    new TopBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            new TableRow(
                CreateImageCell(mainPart, profileImageBytes),
                CreateHeaderTextCell(data),
                CreateHeaderContactCell(mainPart, firstLine, secondLine)));

        return table;
    }

    private static TableCell CreateImageCell(MainDocumentPart mainPart, byte[]? imageBytes)
    {
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "1300" },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top },
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None })));

        var paragraph = CreateParagraphWithSpacing(0, 80);
        if (imageBytes is not null)
        {
            paragraph.Append(CreateImageRun(mainPart, imageBytes, 90, 90, circleCrop: true));
        }

        cell.Append(paragraph);
        return cell;
    }

    private static TableCell CreateHeaderTextCell(ResumeData data)
    {
        var cellProps = new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "3500" },
            new TableCellMargin(
                new LeftMargin { Width = "240", Type = TableWidthUnitValues.Dxa }),
            new TableCellBorders(
                new TopBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.Single, Size = 14U, Color = Navy },
                new BottomBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None }));

        var cell = new TableCell(cellProps);

        cell.Append(CreateStyledParagraph(
            $"{data.Profile.FirstName} {data.Profile.LastName}".Trim(),
            fontSize: 52,
            bold: true,
            color: Navy,
            spacingAfter: 40));

        var headline = string.IsNullOrWhiteSpace(data.Profile.Headline) ? "Softwareentwickler" : data.Profile.Headline.Trim();
        cell.Append(CreateStyledParagraph(
            headline,
            fontSize: 24,
            bold: true,
            italic: true,
            color: Teal,
            spacingAfter: 20));

        return cell;
    }

    private static TableCell CreateHeaderContactCell(MainDocumentPart mainPart, IReadOnlyList<ContactEntry> firstLine, IReadOnlyList<ContactEntry> secondLine)
    {
        var cellProps = new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "5466" },
            new TableCellMargin(new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa }),
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
            new TableCellBorders(
                new TopBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None }));

        var cell = new TableCell(cellProps);

        if (firstLine.Count > 0)
        {
            cell.Append(CreateContactParagraph(mainPart, firstLine, 9));
        }

        if (secondLine.Count > 0)
        {
            cell.Append(CreateContactParagraph(mainPart, secondLine, 0));
        }

        return cell;
    }

    private static Paragraph CreateContactParagraph(MainDocumentPart mainPart, IReadOnlyList<ContactEntry> contacts, int spacingAfter)
    {
        var paragraph = CreateParagraphWithSpacing(0, spacingAfter);

        for (var i = 0; i < contacts.Count; i++)
        {
            if (i > 0)
            {
                paragraph.Append(new Run(CreateRunProperties(fontSize: 17, color: Muted), new Text("   ")));
            }

            var iconBytes = LoadIconBytes(contacts[i].IconKind);
            if (iconBytes is not null)
            {
                paragraph.Append(CreateImageRun(mainPart, iconBytes, 11, 11, circleCrop: false));
                paragraph.Append(new Run(CreateRunProperties(fontSize: 17, color: Muted), new Text(" ")));
            }

            var color = contacts[i].IconKind is ContactIconKind.LinkedIn or ContactIconKind.Github ? Muted : BodyColor;
            paragraph.Append(new Run(CreateRunProperties(fontSize: 18, color: color), new Text(contacts[i].Text)));
        }

        return paragraph;
    }

    private static void AppendOrderedMainSections(Body body, ResumeData data)
    {
        foreach (var sectionKey in CvStudioMainSectionOrder.Resolve(data.ContentSectionOrder))
        {
            switch (sectionKey)
            {
                case CvStudioMainSectionOrder.Summary:
                    if (!string.IsNullOrWhiteSpace(data.Profile.Summary))
                    {
                        body.Append(CreateSectionTitle(CvSectionTitleResolver.QualificationsProfile(data)));
                        body.Append(CreateStyledParagraph(data.Profile.Summary, 21, color: BodyColor, spacingAfter: 80));
                    }
                    break;
                case CvStudioMainSectionOrder.Skills:
                    AppendKnowledge(body, data);
                    break;
                case CvStudioMainSectionOrder.Work:
                    AppendWork(body, data);
                    break;
                case CvStudioMainSectionOrder.Education:
                    AppendEducation(body, data);
                    break;
                case CvStudioMainSectionOrder.Languages:
                    AppendStandaloneLanguages(body, data);
                    break;
                case CvStudioMainSectionOrder.Interests:
                    AppendStandaloneInterests(body, data);
                    break;
                case CvStudioMainSectionOrder.Projects:
                    break;
            }
        }
    }

    private static void AppendKnowledge(Body body, ResumeData data)
    {
        var groups = data.Skills
            .Where(g => !string.IsNullOrWhiteSpace(g.CategoryName) && !IsLanguageCategory(g.CategoryName) && !IsLinkCategory(g.CategoryName))
            .Select(g => new { Group = g, Items = JoinItemsWithDot(g.Items) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Items))
            .ToList();

        if (groups.Count == 0)
        {
            return;
        }

        body.Append(CreateSectionTitle(CvSectionTitleResolver.Skills(data)));
        var table = new Table(
            new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableBorders(
                    new TopBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new BottomBorder { Val = BorderValues.None },
                    new RightBorder { Val = BorderValues.None },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })));

        for (var i = 0; i < groups.Count; i++)
        {
            var row = groups[i];
            var isLast = i == groups.Count - 1;

            table.Append(new TableRow(
                CreateKnowledgeCell(row.Group.CategoryName.Trim(), 2200, true, isLast),
                CreateKnowledgeCell(row.Items, 8266, false, isLast)));
        }

        body.Append(table);
    }

    private static void AppendWork(Body body, ResumeData data)
    {
        var items = data.WorkItems;
        if (items.Count == 0)
        {
            return;
        }

        body.Append(CreateSectionTitle(CvSectionTitleResolver.WorkExperience(data)));

        foreach (var item in items)
        {
            var (company, location) = SplitByPipe(item.Company);
            var period = BuildDateRange(item.StartDate, item.EndDate);

            var companyParagraph = CreateParagraphWithTabStop(0, 40, 10466 * 20);
            companyParagraph.Append(new Run(CreateRunProperties(21, Navy, bold: true), new Text(company)));
            companyParagraph.Append(new Run(new TabChar()));
            companyParagraph.Append(new Run(CreateRunProperties(19, Muted, italic: true), new Text(period)));
            body.Append(companyParagraph);

            if (!string.IsNullOrWhiteSpace(location) || !string.IsNullOrWhiteSpace(item.Role))
            {
                var secondLine = CreateParagraphWithSpacing(0, 35);
                if (!string.IsNullOrWhiteSpace(location))
                {
                    secondLine.Append(new Run(CreateRunProperties(18, Muted), new Text(location.Trim())));
                }

                if (!string.IsNullOrWhiteSpace(item.Role))
                {
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        secondLine.Append(new Run(CreateRunProperties(18, Muted), new Text("   ")));
                    }

                    secondLine.Append(new Run(CreateRunProperties(19, Teal, bold: true, italic: true), new Text(item.Role.Trim())));
                }

                body.Append(secondLine);
            }

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                body.Append(CreateStyledParagraph(item.Description.Trim(), 18, italic: true, color: Muted, spacingAfter: 45));
            }

            foreach (var bullet in item.Bullets.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                body.Append(CreateStyledParagraph($"� {bullet.Trim()}", 19, color: BodyColor, spacingAfter: 35, leftIndent: 280));
            }

            body.Append(CreateParagraphWithSpacing(0, 80));
        }
    }

    private static void AppendEducation(Body body, ResumeData data)
    {
        var items = data.EducationItems;
        if (items.Count == 0)
        {
            return;
        }

        body.Append(CreateSectionTitle(CvSectionTitleResolver.Education(data)));

        foreach (var item in items)
        {
            var (school, location) = SplitByPipe(item.School);
            var period = BuildDateRange(item.StartDate, item.EndDate);

            var schoolParagraph = CreateParagraphWithTabStop(0, 20, 10466 * 20);
            schoolParagraph.Append(new Run(CreateRunProperties(21, BodyColor, bold: true), new Text(school)));
            if (!string.IsNullOrWhiteSpace(location))
            {
                schoolParagraph.Append(new Run(CreateRunProperties(19, Muted), new Text($"  |  {location}")));
            }

            schoolParagraph.Append(new Run(new TabChar()));
            schoolParagraph.Append(new Run(CreateRunProperties(19, Muted, italic: true), new Text(period)));
            body.Append(schoolParagraph);

            if (!string.IsNullOrWhiteSpace(item.Degree))
            {
                body.Append(CreateStyledParagraph(item.Degree, 20, bold: true, italic: true, color: Teal, spacingAfter: 80));
            }

            if (!string.IsNullOrWhiteSpace(item.Description))
                body.Append(CreateStyledParagraph(item.Description.Trim(), 18, italic: true, color: Muted, spacingAfter: 60));
        }
    }

    private static void AppendStandaloneLanguages(Body body, ResumeData data)
    {
        string languageLine;
        if (data.LanguageItems is { Count: > 0 })
        {
            languageLine = string.Join(" - ", data.LanguageItems
                .Where(static x => !string.IsNullOrWhiteSpace(x.Label))
                .Select(x =>
                {
                    var label = x.Label.Trim();
                    return string.IsNullOrWhiteSpace(x.Level) ? label : $"{label} ({x.Level.Trim()})";
                }));
        }
        else
        {
            languageLine = string.Join(" - ", data.Skills
                .Where(g => IsLanguageCategory(g.CategoryName))
                .SelectMany(g => g.Items)
                .Where(static x => !string.IsNullOrWhiteSpace(x)));
        }

        if (string.IsNullOrWhiteSpace(languageLine))
            return;

        body.Append(CreateSectionTitle(CvSectionTitleResolver.Languages(data)));
        body.Append(CreateStyledParagraph(languageLine, 19, color: BodyColor, spacingAfter: 80));
    }

    private static void AppendStandaloneInterests(Body body, ResumeData data)
    {
        var interestsLine = string.Join(" - ", data.Hobbies.Where(static x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(interestsLine))
            return;

        body.Append(CreateSectionTitle(CvSectionTitleResolver.Interests(data)));
        body.Append(CreateStyledParagraph(interestsLine, 19, color: BodyColor, spacingAfter: 80));
    }

    private static Paragraph CreateSectionTitle(string text)
    {
        var paragraph = CreateParagraphWithSpacing(120, 70);
        paragraph.ParagraphProperties ??= new ParagraphProperties();

        paragraph.ParagraphProperties.ParagraphBorders = new ParagraphBorders(
            new TopBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.Single, Size = 18U, Color = Navy },
            new BottomBorder { Val = BorderValues.Single, Size = 2U, Color = Rule },
            new RightBorder { Val = BorderValues.None });

        paragraph.ParagraphProperties.Shading = new Shading
        {
            Val = ShadingPatternValues.Clear,
            Fill = SectionBg
        };

        paragraph.Append(new Run(CreateRunProperties(22, Navy, bold: true), new Text($"  {text}")));
        return paragraph;
    }

    private static Paragraph CreateRuleParagraph(string color, uint size)
    {
        var paragraph = CreateParagraphWithSpacing(0, 120);
        paragraph.ParagraphProperties ??= new ParagraphProperties();
        paragraph.ParagraphProperties.ParagraphBorders = new ParagraphBorders(
            new TopBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.Single, Size = size, Color = color },
            new RightBorder { Val = BorderValues.None });
        return paragraph;
    }

    private static Paragraph CreateStyledParagraph(
        string? text,
        int fontSize,
        bool bold = false,
        bool italic = false,
        string color = BodyColor,
        int spacingBefore = 0,
        int spacingAfter = 40,
        int leftIndent = 0)
    {
        var paragraph = CreateParagraphWithSpacing(spacingBefore, spacingAfter, leftIndent: leftIndent);
        paragraph.Append(new Run(CreateRunProperties(fontSize, color, bold, italic), new Text(text ?? string.Empty)
        {
            Space = SpaceProcessingModeValues.Preserve
        }));
        return paragraph;
    }

    private static Paragraph CreateParagraphWithSpacing(int before, int after, int leftIndent = 0)
    {
        var paragraphProperties = new ParagraphProperties(
            new SpacingBetweenLines
            {
                Before = before.ToString(),
                After = after.ToString(),
                Line = "300",
                LineRule = LineSpacingRuleValues.Auto
            });

        if (leftIndent > 0)
        {
            paragraphProperties.Indentation = new Indentation { Left = leftIndent.ToString() };
        }

        return new Paragraph(paragraphProperties);
    }

    private static Paragraph CreateParagraphWithTabStop(int before, int after, int tabPosition)
    {
        var paragraphProperties = new ParagraphProperties(
            new Tabs(new TabStop { Val = TabStopValues.Right, Position = tabPosition }),
            new SpacingBetweenLines
            {
                Before = before.ToString(),
                After = after.ToString(),
                Line = "300",
                LineRule = LineSpacingRuleValues.Auto
            });

        return new Paragraph(paragraphProperties);
    }

    private static RunProperties CreateRunProperties(int fontSize, string color, bool bold = false, bool italic = false)
    {
        var props = new RunProperties(
            new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "Calibri" },
            new FontSize { Val = fontSize.ToString() },
            new Color { Val = color });

        if (bold)
        {
            props.Append(new Bold());
        }

        if (italic)
        {
            props.Append(new Italic());
        }

        return props;
    }

    private static Run CreateImageRun(MainDocumentPart mainPart, byte[] imageBytes, int widthPx, int heightPx, bool circleCrop = false)
    {
        var imagePartType = DetectImagePartType(imageBytes);
        var imagePart = mainPart.AddImagePart(imagePartType);

        using (var stream = new MemoryStream(imageBytes))
        {
            imagePart.FeedData(stream);
        }

        var relationshipId = mainPart.GetIdOfPart(imagePart);

        const long emusPerPixel = 9525L;
        var widthEmus = widthPx * emusPerPixel;
        var heightEmus = heightPx * emusPerPixel;
        var drawingId = (uint)Interlocked.Increment(ref _docPropertyCounter);

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmus, Cy = heightEmus },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = drawingId, Name = "ProfilePicture" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = drawingId, Name = "ProfilePicture" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmus, Cy = heightEmus }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = circleCrop ? A.ShapeTypeValues.Ellipse : A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        return new Run(drawing);
    }

    private static PartTypeInfo DetectImagePartType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ImagePartType.Png;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ImagePartType.Jpeg;
        }

        return ImagePartType.Png;
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
        Trace.TraceWarning("{0} {1}: {2}", nameof(OpenXmlDocxGenerator), context, exception.Message);
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
        return string.Join(" � ", items.Where(static x => !string.IsNullOrWhiteSpace(x)).Select(static x => x.Trim()));
    }

    private static TableCell CreateKnowledgeCell(string text, int width, bool isLabel, bool isLast)
    {
        var border = isLast ? BorderValues.None : BorderValues.Single;
        var color = isLabel ? Navy : Muted;
        var runSize = isLabel ? 20 : 19;

        var paragraph = CreateParagraphWithSpacing(0, 0);
        paragraph.Append(new Run(CreateRunProperties(runSize, color, bold: isLabel), new Text(text)));

        return new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width.ToString() },
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None },
                    new BottomBorder { Val = border, Size = 8U, Color = Rule },
                    new RightBorder { Val = BorderValues.None })),
            paragraph);
    }

    private static string BuildDateRange(string startDate, string endDate)
    {
        var start = NormalizeDate(startDate);
        var end = NormalizeDate(endDate);

        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(end))
        {
            return start;
        }

        if (string.IsNullOrWhiteSpace(start))
        {
            return end;
        }

        return $"{start} - {end}";
    }

    private static string NormalizeDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("heute", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("today", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("present", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("aktuell", StringComparison.OrdinalIgnoreCase))
        {
            return "Heute";
        }

        var formats = new[] { "MM/yyyy", "M/yyyy", "MM.yyyy", "M.yyyy", "yyyy-MM", "yyyy" };
        if (DateTime.TryParseExact(trimmed, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        return trimmed;
    }
}


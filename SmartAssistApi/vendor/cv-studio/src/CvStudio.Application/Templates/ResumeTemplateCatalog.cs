using CvStudio.Application.Contracts;
using CvStudio.Application.DTOs;
using CvStudio.Application.Exceptions;

namespace CvStudio.Application.Templates;

/// <summary>
/// Built-in resume templates: only synthetic placeholder data (no real person or employer).
/// Each user receives a fresh copy from the API when creating from a template; nothing here is shared as "live" user data.
/// </summary>
public static class ResumeTemplateCatalog
{
    public const string SoftwareDeveloper = "software-developer";
    public const string ItSupport = "it-support";
    public const string ServiceGeneral = "service-general";

    public static IReadOnlyList<ResumeTemplateDto> List()
    {
        return
        [
            new ResumeTemplateDto
            {
                Key = SoftwareDeveloper,
                DisplayName = "Software Entwickler",
                Description = "Backend/Frontend-orientierter CV mit Projekten und Tech-Stack."
            },
            new ResumeTemplateDto
            {
                Key = ItSupport,
                DisplayName = "IT Supporter",
                Description = "Support-, Infrastruktur- und Incident-Fokus."
            },
            new ResumeTemplateDto
            {
                Key = ServiceGeneral,
                DisplayName = "Service / Gastro / Kommissionierer / Briefzusteller",
                Description = "Allgemeines Service-Profil für operative und kundennahe Tätigkeiten."
            }
        ];
    }

    public static (string Title, ResumeData Data) GetDefaultResume(string templateKey)
    {
        return templateKey switch
        {
            SoftwareDeveloper => ("Vorlage: Softwareentwicklung", CreateSoftwareDeveloperData()),
            ItSupport => ("Vorlage: IT-Support", CreateItSupportData()),
            ServiceGeneral => ("Vorlage: Service & Logistik", CreateServiceData()),
            _ => throw new NotFoundException($"Template '{templateKey}' was not found.")
        };
    }

    private static ResumeData CreateSoftwareDeveloperData()
    {
        return new ResumeData
        {
            Profile = new ProfileData
            {
                FirstName = "Vorname",
                LastName = "Nachname",
                Headline = "Softwareentwicklerin (Beispiel)",
                Email = "bewerbung@example.com",
                Phone = "+49 000 000000",
                Location = "Musterstadt, Deutschland",
                ProfileImageUrl = "",
                Summary = "Beispielprofil: Entwicklung von Web-APIs und UI-Komponenten mit Fokus auf Lesbarkeit, Tests und Teamarbeit. Platzhaltertext — bitte durch Ihre echte Kurzbeschreibung ersetzen."
            },
            WorkItems =
            [
                new WorkItemData
                {
                    Company = "Beispiel IT-Dienstleister GmbH | Musterstadt",
                    Role = "Junior Softwareentwicklung",
                    StartDate = "03/2023",
                    EndDate = "Heute",
                    Description = "Beispielaufgaben: Feature-Teams, Code-Reviews, CI.",
                    Bullets =
                    [
                        "API-Endpunkte in einem internen Dienst mit dokumentierten Schnittstellen mitentwickelt.",
                        "Unit-Tests und Refactorings zur Reduktion von Duplikaten umgesetzt.",
                        "Enge Zusammenarbeit mit Product Owner und QA nach Scrum."
                    ]
                },
                new WorkItemData
                {
                    Company = "Musterfirma Digital AG | Musterstadt",
                    Role = "Werkstudent Entwicklung",
                    StartDate = "09/2021",
                    EndDate = "02/2023",
                    Description = "Beispiel: Unterstützung bei Frontend- und Backend-Themen.",
                    Bullets =
                    [
                        "Kleinere Tickets in TypeScript/React und REST-Clients bearbeitet.",
                        "Fehleranalysen in Staging-Umgebungen und Logging-Auswertungen unterstützt."
                    ]
                }
            ],
            EducationItems =
            [
                new EducationItemData
                {
                    School = "Hochschule Musterstadt",
                    Degree = "Bachelor Informatik (Beispiel)",
                    StartDate = "10/2018",
                    EndDate = "09/2021"
                }
            ],
            Skills =
            [
                new SkillGroupData { CategoryName = "Programmierung", Items = ["C#", "TypeScript", "REST", "SQL", "Git"] },
                new SkillGroupData { CategoryName = "Methodik", Items = ["Code Reviews", "Testing", "Agile Zusammenarbeit"] },
                new SkillGroupData { CategoryName = "Sprachen", Items = ["Deutsch (Beispiel C1)", "Englisch (Beispiel B2)"] },
                new SkillGroupData { CategoryName = "Links (Platzhalter)", Items = ["https://example.com/portfolio", "https://example.com/code"] }
            ],
            Hobbies = ["Beispiel: Open Source", "Technikblogs"]
        };
    }

    private static ResumeData CreateItSupportData()
    {
        return new ResumeData
        {
            Profile = new ProfileData
            {
                FirstName = "Vorname",
                LastName = "Nachname",
                Headline = "IT-Support / Service (Beispiel)",
                Email = "bewerbung@example.com",
                Phone = "+49 000 000000",
                Location = "Musterstadt, Deutschland",
                ProfileImageUrl = "",
                Summary = "Beispielprofil: Anwenderbetreuung, Tickets und dokumentierte Lösungen. Platzhalter — bitte anpassen."
            },
            WorkItems =
            [
                new WorkItemData
                {
                    Company = "Musterfirma Support GmbH | Musterstadt",
                    Role = "1st-Level Support",
                    StartDate = "01/2023",
                    EndDate = "Heute",
                    Bullets =
                    [
                        "Tickets in einem ITSM-Tool entgegengenommen und nach Priorität bearbeitet.",
                        "Standardarbeitsplätze (Windows, Office) und Drucker/Netzwerk-Grundlagen betreut.",
                        "Anleitungen für wiederkehrende Fragen in der Wissensdatenbank gepflegt."
                    ]
                },
                new WorkItemData
                {
                    Company = "Beispiel Callcenter AG | Musterstadt",
                    Role = "Kundenberatung (Telefon/E-Mail)",
                    StartDate = "06/2021",
                    EndDate = "12/2022",
                    Bullets =
                    [
                        "Kundenanfragen strukturiert dokumentiert und Eskalationsregeln eingehalten.",
                        "Qualitätsziele im Team-Reporting berücksichtigt."
                    ]
                }
            ],
            EducationItems =
            [
                new EducationItemData
                {
                    School = "Berufskolleg Musterstadt",
                    Degree = "Fachabitur / IT-Assistent (Beispiel)",
                    StartDate = "08/2018",
                    EndDate = "06/2021"
                }
            ],
            Skills =
            [
                new SkillGroupData { CategoryName = "IT-Support", Items = ["Windows", "Office", "Ticketsysteme", "Remote-Support"] },
                new SkillGroupData { CategoryName = "Soft Skills", Items = ["Kundenorientierung", "Dokumentation", "Teamarbeit"] },
                new SkillGroupData { CategoryName = "Sprachen", Items = ["Deutsch (Beispiel)", "Englisch (Beispiel)"] }
            ],
            Hobbies = ["Beispiel: Sport", "Lesen"]
        };
    }

    private static ResumeData CreateServiceData()
    {
        return new ResumeData
        {
            Profile = new ProfileData
            {
                FirstName = "Vorname",
                LastName = "Nachname",
                Headline = "Servicekraft / Logistik (Beispiel)",
                Email = "bewerbung@example.com",
                Phone = "+49 000 000000",
                Location = "Musterstadt, Deutschland",
                ProfileImageUrl = "",
                WorkPermit = "Beispielhinweis — falls zutreffend, Text ersetzen",
                Summary = "Beispielprofil: Kundenservice, Zustellung und zuverlässige Schichtarbeit. Platzhalter — bitte durch Ihre echten Stationen ersetzen."
            },
            WorkItems =
            [
                new WorkItemData
                {
                    Company = "Muster Logistik GmbH | Musterstadt",
                    Role = "Kommissionierung / Lager",
                    StartDate = "04/2024",
                    EndDate = "Heute",
                    Bullets =
                    [
                        "Picklisten nach Scanner und Mengenvorgaben bearbeitet.",
                        "Qualitätskontrolle bei Wareneingang stichprobenartig mit dokumentiert."
                    ]
                },
                new WorkItemData
                {
                    Company = "Beispiel Gastronomie OHG | Musterstadt",
                    Role = "Servicekraft",
                    StartDate = "03/2022",
                    EndDate = "03/2024",
                    Bullets =
                    [
                        "Gästebetreuung, Kasse und Hygienevorgaben im Team eingehalten.",
                        "Flexible Einsätze an Wochenenden und Feiertagen."
                    ]
                },
                new WorkItemData
                {
                    Company = "Muster Zustellung | Musterstadt",
                    Role = "Zustellung / Aushilfe",
                    StartDate = "01/2020",
                    EndDate = "02/2022",
                    Bullets =
                    [
                        "Tourbezogene Zustellung und Übergabeprotokolle nach Vorgabe.",
                        "Zuverlässige Bargeld- und Sendungsabwicklung (Beispiel)."
                    ]
                }
            ],
            EducationItems =
            [
                new EducationItemData
                {
                    School = "Schule Musterstadt",
                    Degree = "Mittlere Reife (Beispiel)",
                    StartDate = "08/2014",
                    EndDate = "06/2018"
                }
            ],
            Skills =
            [
                new SkillGroupData { CategoryName = "Kundenservice", Items = ["Beratung", "Beschwerdemanagement", "Kassensysteme"] },
                new SkillGroupData { CategoryName = "Logistik", Items = ["Kommissionierung", "Scanner", "Lagerverwaltung"] },
                new SkillGroupData { CategoryName = "Arbeitsweise", Items = ["Zuverlässigkeit", "Pünktlichkeit", "Teamarbeit"] },
                new SkillGroupData { CategoryName = "Sprachen", Items = ["Deutsch (Beispiel)", "Englisch (Beispiel)"] }
            ],
            Hobbies = []
        };
    }
}

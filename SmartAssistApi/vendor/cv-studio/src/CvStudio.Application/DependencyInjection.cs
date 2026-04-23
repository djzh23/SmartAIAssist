using Microsoft.Extensions.DependencyInjection;
using CvStudio.Application.Services;

namespace CvStudio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IResumeService, ResumeService>();
        services.AddScoped<ISnapshotService, SnapshotService>();
        services.AddScoped<IPdfExportService, PdfExportService>();
        services.AddScoped<IDocxExportService, DocxExportService>();
        services.AddScoped<IAtsScoreService, AtsScoreService>();
        services.AddScoped<IResumeAtsAnalyzer, ResumeAtsAnalyzer>();

        return services;
    }
}

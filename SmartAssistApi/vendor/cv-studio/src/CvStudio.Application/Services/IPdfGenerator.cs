namespace CvStudio.Application.Services;

public interface IPdfGenerator
{
    byte[] GenerateFromResumeJson(string resumeJson, PdfDesign design = PdfDesign.DesignA);
}


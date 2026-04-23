namespace CvStudio.Application.Services;

public interface IDocxGenerator
{
    byte[] GenerateFromResumeJson(string resumeJson);
}


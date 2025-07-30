namespace ArtaleAI.Config
{
    public interface IConfigEventHandler
    {
        void OnConfigLoaded(AppConfig config);
        void OnConfigSaved(AppConfig config);
        void OnConfigError(string errorMessage);
    }
}

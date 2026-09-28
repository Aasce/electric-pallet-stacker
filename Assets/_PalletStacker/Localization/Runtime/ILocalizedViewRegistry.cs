namespace ElectricPalletStackers.Localization
{
    public interface ILocalizedViewRegistry
    {
        void Register(ILocalizedView view);
        void Unregister(ILocalizedView view);
        void RefreshAll();
    }
}

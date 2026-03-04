namespace AssetInventory
{
    public interface IActionProgress<T>
    {
        T WithProgress(string caption);
    }
}

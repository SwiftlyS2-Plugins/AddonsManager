namespace AddonsManager;

public partial class AddonsManager
{
    public string GenerateProgressBar(float progress)
    {
        int progressAsInteger = (int)Math.Floor(progress);

        string progressBar = "[";
        for (int i = 0; i < 50; i++)
        {
            progressBar += i < progressAsInteger / 2 ? "■" : "-";
        }
        progressBar += "]";
        return progressBar;
    }
}
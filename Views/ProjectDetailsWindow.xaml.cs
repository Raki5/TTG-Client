using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using TTGClient.Models;

namespace TTGClient.Views;

public partial class ProjectDetailsWindow : Window
{
    private readonly ModProject _project;

    public ProjectDetailsWindow(ModProject project)
    {
        InitializeComponent();
        _project = project;
        Title = $"{project.Title} · TTG Client";
        ProjectTitleText.Text = project.Title;
        ProjectSourceText.Text = $"{project.SourceName}  ·  {project.Author}";
        ProjectDescriptionText.Text = string.IsNullOrWhiteSpace(project.Description) ? "No description was supplied by the source API." : project.Description;
        DownloadsText.Text = project.FormattedDownloads;
        UpdatedText.Text = project.ModifiedAt?.LocalDateTime.ToString("dd MMM yyyy") ?? "Unknown";
        VersionsText.Text = project.SupportedVersions.Count == 0 ? "Not listed" : string.Join(", ", project.SupportedVersions.Take(12));
        CategoriesText.Text = project.Categories.Count == 0 ? "Not listed" : string.Join(", ", project.Categories.Take(8));

        if (Uri.TryCreate(project.IconUrl, UriKind.Absolute, out var iconUri) && (iconUri.Scheme == Uri.UriSchemeHttps || iconUri.Scheme == Uri.UriSchemeHttp))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = iconUri;
                image.DecodePixelWidth = 120;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                ProjectIconImage.Source = image;
            }
            catch { /* Use the icon placeholder if an image cannot be loaded. */ }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_project.ProjectUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            MessageBox.Show(this, "The project page URL isn't valid.", "TTG Client", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open the project page.\n\n{ex.Message}", "TTG Client", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

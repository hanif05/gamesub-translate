using System.Windows;
using GameSubTranslate.App.Profiles;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;

namespace GameSubTranslate.App;

public partial class MainWindow : Window
{
    private readonly ProfileRepository _repo;
    private readonly Database _db;

    public MainWindow() : this(new Database(), null) { }

    public MainWindow(Database db, Window? owner)
    {
        InitializeComponent();
        _db = db;
        _repo = new ProfileRepository(db);
        if (owner is not null) Owner = owner;
        _db.EnsureSchema();
        Refresh();
    }

    private void Refresh()
    {
        var profiles = _repo.GetAll().ToList();
        ProfileList.ItemsSource = profiles;
        CountText.Text = $"({profiles.Count})";
    }

    private GameProfile? Selected => ProfileList.SelectedItem as GameProfile;

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileEditWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _repo.Create(dlg.Result);
            Refresh();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        // Reload fresh from DB so regions list is current.
        var current = _repo.GetById(sel.Id);
        if (current is null) return;
        var dlg = new ProfileEditWindow(current) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _repo.Update(dlg.Result);
            Refresh();
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        var current = _repo.GetById(sel.Id);
        if (current is null) return;
        current.Id = 0;
        current.Name = current.Name + " (copy)";
        foreach (var r in current.Regions) r.Id = 0;
        _repo.Create(current);
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        var ok = System.Windows.MessageBox.Show(this,
            $"Delete profile \"{sel.Name}\"? This removes all its regions.",
            "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;
        _repo.Delete(sel.Id);
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
}

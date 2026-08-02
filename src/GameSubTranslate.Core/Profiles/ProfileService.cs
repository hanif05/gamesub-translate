using GameSubTranslate.Config;

namespace GameSubTranslate.Profiles;

/// <summary>
/// In-memory state for the currently active profile + region, persisted to AppSettings
/// so the selection survives restarts. Pipeline consumers read ActiveRegion() each cycle.
/// </summary>
public sealed class ProfileService
{
    private readonly ProfileRepository _repo;
    private readonly SettingsStore _settings;
    private readonly AppSettings _app;

    public int? ActiveProfileId { get; private set; }
    public int? ActiveRegionId { get; private set; }

    public ProfileService(ProfileRepository repo, SettingsStore settings, AppSettings app)
    {
        _repo = repo;
        _settings = settings;
        _app = app;

        // Restore last-active state from settings, validating it still exists.
        if (_app.ActiveProfileId is int pid && _repo.GetById(pid) is not null)
        {
            ActiveProfileId = pid;
            var regions = _repo.GetById(pid)!.Regions;
            if (_app.ActiveRegionId is int rid && regions.Any(r => r.Id == rid))
                ActiveRegionId = rid;
            else
                ActiveRegionId = regions.FirstOrDefault(r => r.IsActiveDefault)?.Id ?? regions.FirstOrDefault()?.Id;
        }
    }

    public GameProfile? ActiveProfile =>
        ActiveProfileId is int id ? _repo.GetById(id) : null;

    public CaptureRegion? ActiveRegion()
    {
        var profile = ActiveProfile;
        if (profile is null) return null;
        return ActiveRegionId is int rid
            ? profile.Regions.FirstOrDefault(r => r.Id == rid)
            : profile.Regions.FirstOrDefault(r => r.IsActiveDefault) ?? profile.Regions.FirstOrDefault();
    }

    /// <summary>Select the active profile and default its active region. Persists to settings.</summary>
    public void SetActiveProfile(int profileId)
    {
        var p = _repo.GetById(profileId);
        if (p is null) return;
        ActiveProfileId = profileId;
        ActiveRegionId = p.Regions.FirstOrDefault(r => r.IsActiveDefault)?.Id ?? p.Regions.FirstOrDefault()?.Id;
        Persist();
    }

    /// <summary>Clear the active profile + region (e.g. after it was deleted). Persists.</summary>
    public void ClearActiveProfile()
    {
        ActiveProfileId = null;
        ActiveRegionId = null;
        Persist();
    }

    /// <summary>Select the active region within the current profile. Persists to settings.</summary>
    public void SetActiveRegion(int regionId)
    {
        var profile = ActiveProfile;
        if (profile is null || !profile.Regions.Any(r => r.Id == regionId)) return;
        ActiveRegionId = regionId;
        Persist();
    }

    private void Persist()
    {
        _app.ActiveProfileId = ActiveProfileId;
        _app.ActiveRegionId = ActiveRegionId;
        _settings.Save(_app);
    }
}

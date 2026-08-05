using Microsoft.AspNetCore.Components.Forms;

namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

public interface IPhotoStorageService
{
    Task<string> SaveAsync(Guid staffId, IBrowserFile file, CancellationToken ct = default);
    Task DeleteAsync(string relativePath);
}

public class PhotoStorageService : IPhotoStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public PhotoStorageService(IWebHostEnvironment env, IConfiguration cfg) { _env = env; _cfg = cfg; }

    public async Task<string> SaveAsync(Guid staffId, IBrowserFile file, CancellationToken ct = default)
    {
        var maxBytes = _cfg.GetValue<long>("FileUpload:MaxPhotoSizeBytes", 2_097_152);
        if (file.Size > maxBytes) throw new InvalidOperationException($"Photo exceeds {maxBytes / 1024 / 1024} MB limit.");

        var relDir = _cfg.GetValue<string>("FileUpload:PhotoStorageRelativePath") ?? "uploads/photos";
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);

        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new InvalidOperationException("Only JPG, PNG, or WebP images are allowed.");

        var fileName = $"{staffId}{ext}";
        var absPath = Path.Combine(absDir, fileName);
        await using var fs = File.Create(absPath);
        await file.OpenReadStream(maxBytes, ct).CopyToAsync(fs, ct);

        return $"{relDir}/{fileName}";
    }

    public Task DeleteAsync(string relativePath)
    {
        var abs = Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(abs)) File.Delete(abs);
        return Task.CompletedTask;
    }
}

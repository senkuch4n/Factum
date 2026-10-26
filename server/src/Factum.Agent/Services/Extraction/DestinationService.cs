using System.Text.Json.Serialization;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Extraction;

// extraccion-logica-android §5.10 / §8.2.3. DTOs del navegador de destinos.

public sealed class DriveDto
{
    public string Kind { get; set; } = "internal";   // internal | removable | network
    public string Label { get; set; } = "";
    public string RootPath { get; set; } = "";
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }
    public string FileSystem { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MaxFileBytes { get; set; }
}

public sealed class DestinationsResponse
{
    public bool Restricted { get; set; }
    public bool CanReveal { get; set; }
    public string DefaultPath { get; set; } = "";
    public List<DriveDto> Drives { get; set; } = [];
}

public sealed class BrowseEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class BrowseResponse
{
    public string Path { get; set; } = "";
    public string? Parent { get; set; }
    public List<BrowseEntry> Entries { get; set; } = [];
}

public sealed class CheckResponse
{
    public string Path { get; set; } = "";
    public bool Ok { get; set; }
    public bool Writable { get; set; }
    public long FreeBytes { get; set; }
    public long RequiredBytes { get; set; }
    public long MarginBytes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MissingBytes { get; set; }
    public string FileSystem { get; set; } = "";
    public string FinalFolderPreview { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? MaxFileBytes { get; set; }
}

/// <summary>
/// Unidades, navegación y chequeo de destino (extraccion-logica-android §5.10). Con <c>AllowedRoots</c> (Docker)
/// solo se ofrecen esas raíces y los montajes que cuelgan de ellas, y <c>browse</c>/<c>check</c> rechazan lo que
/// quede fuera. Puro salvo el acceso a <see cref="DriveInfo"/> / sistema de archivos; la validación de raíces es
/// testeable con <see cref="IsUnderAllowedRoots"/>.
/// </summary>
public sealed class DestinationService(IOptions<AgentOptions> opts, IAgentDiskProbe probe)
{
    public const long Fat32MaxFile = 4294967295;
    private static readonly string[] Fat32 = ["FAT", "FAT32", "vfat", "msdos", "exfat_is_not_fat"];

    private ExtractionOptions Ext => opts.Value.Extraction;
    public bool Restricted => Ext.AllowedRoots.Length > 0;

    private static readonly string[] LinuxPseudoFs =
    [
        "proc", "sysfs", "tmpfs", "devtmpfs", "overlay", "squashfs", "autofs", "devpts", "securityfs", "pstore",
        "bpf", "tracefs", "fusectl", "configfs", "debugfs", "mqueue", "hugetlbfs", "ramfs", "nsfs",
    ];
    private static readonly string[] LinuxPseudoMountPrefixes =
        ["/snap", "/boot", "/sys", "/proc", "/dev"];

    public string DefaultPath => Path.Combine(opts.Value.ResolveEvidenceDirectory(), "Extracciones");

    public DestinationsResponse GetDestinations()
    {
        var resp = new DestinationsResponse
        {
            Restricted = Restricted,
            CanReveal = !Restricted,   // en Docker no se puede abrir la carpeta (§8.2.5 reveal)
            DefaultPath = DefaultPath,
        };
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!SafeIsReady(d)) continue;
            var root = d.RootDirectory.FullName;
            if (Restricted && !IsUnderAllowedRoots(root, Ext.AllowedRoots)) continue;
            if (!Restricted && OperatingSystem.IsLinux() && !UsefulLinuxMount(d, root)) continue;
            resp.Drives.Add(ToDrive(d));
        }
        return resp;
    }

    public BrowseResponse Browse(string path)
    {
        var full = ResolveAllowed(path);
        if (!Directory.Exists(full))
            throw new AgentHttpException(404, AgentErrorCodes.PathNotFound, "La carpeta no existe.");
        var entries = new List<BrowseEntry>();
        foreach (var dir in Directory.EnumerateDirectories(full))
        {
            var info = new DirectoryInfo(dir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;       // no symlinks/junctions
            var name = info.Name;
            if (name.StartsWith('.') || name is "$RECYCLE.BIN" or "System Volume Information") continue;
            if (Restricted && !IsUnderAllowedRoots(dir, Ext.AllowedRoots)) continue;
            entries.Add(new BrowseEntry { Name = name, Path = dir });
        }
        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var parent = Directory.GetParent(full)?.FullName;
        if (Restricted && parent is not null && !IsUnderAllowedRoots(parent, Ext.AllowedRoots)) parent = null;
        return new BrowseResponse { Path = full, Parent = parent, Entries = entries };
    }

    public CheckResponse Check(string path, long requiredBytes, string? manufacturer = null, string? model = null)
    {
        var full = ResolveAllowed(path);
        var resp = new CheckResponse { Path = full, RequiredBytes = Math.Max(0, requiredBytes) };

        if (!Directory.Exists(full) || new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            resp.Ok = false;
            resp.Reason = "path_not_found";
            resp.FinalFolderPreview = PreviewFolder(full, manufacturer, model);
            return resp;
        }

        resp.FileSystem = DriveFormat(full);
        if (IsFat32(resp.FileSystem)) resp.MaxFileBytes = Fat32MaxFile;
        resp.Writable = TryWriteProbe(full);
        resp.FreeBytes = probe.GetAvailableFreeBytes(full) ?? 0;
        resp.MarginBytes = Math.Max(opts.Value.MinFreeBytes, (long)(resp.FreeBytes * 0.05));
        resp.FinalFolderPreview = PreviewFolder(full, manufacturer, model);

        if (!resp.Writable) { resp.Ok = false; resp.Reason = "not_writable"; return resp; }

        var need = resp.RequiredBytes + resp.MarginBytes;
        if (resp.FreeBytes < need)
        {
            resp.Ok = false;
            resp.Reason = "insufficient_space";
            resp.MissingBytes = need - resp.FreeBytes;
            return resp;
        }
        resp.Ok = true;
        return resp;
    }

    /// <summary>Resuelve el destino final a <paramref name="path"/> (ya validado) + carpeta única (no existe).</summary>
    public string ResolveFinalFolder(string allowedFullPath, string? manufacturer, string? model)
    {
        var baseName = ExtractionFolderNames.Build(manufacturer, model, DateTime.Now);
        var unique = ExtractionFolderNames.Unique(baseName, n => Directory.Exists(Path.Combine(allowedFullPath, n)));
        return Path.Combine(allowedFullPath, unique);
    }

    /// <summary>Normaliza y, en Docker, rechaza lo que quede fuera de <c>AllowedRoots</c> (403).</summary>
    public string ResolveAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Falta la ruta.");
        string full;
        try { full = Path.GetFullPath(path); }
        catch { throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Ruta inválida."); }
        if (Restricted && !IsUnderAllowedRoots(full, Ext.AllowedRoots))
            throw new AgentHttpException(403, AgentErrorCodes.PathNotAllowed,
                "La carpeta queda fuera de las permitidas.");
        return full;
    }

    // ── helpers puros / de sistema ──

    /// <summary>true si <paramref name="path"/> es una de las raíces o cuelga de alguna (tras GetFullPath).</summary>
    public static bool IsUnderAllowedRoots(string path, IReadOnlyList<string> roots)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch { return false; }
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var r in roots)
        {
            string root;
            try { root = Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar); } catch { continue; }
            if (root.Length == 0) root = Path.DirectorySeparatorChar.ToString();
            if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root, cmp)) return true;
            if (full.StartsWith(root + Path.DirectorySeparatorChar, cmp)) return true;
        }
        return false;
    }

    public static bool IsFat32(string fs) =>
        fs.Equals("FAT", StringComparison.OrdinalIgnoreCase) ||
        fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ||
        fs.Equals("vfat", StringComparison.OrdinalIgnoreCase) ||
        fs.Equals("msdos", StringComparison.OrdinalIgnoreCase);

    private string PreviewFolder(string full, string? manufacturer, string? model) =>
        Path.Combine(full, ExtractionFolderNames.Build(manufacturer, model, DateTime.Now));

    private static bool SafeIsReady(DriveInfo d) { try { return d.IsReady; } catch { return false; } }

    private static string DriveFormat(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(path) ?? path).DriveFormat; } catch { return ""; }
    }

    private static bool UsefulLinuxMount(DriveInfo d, string root)
    {
        var fs = SafeFormat(d);
        if (LinuxPseudoFs.Contains(fs) || fs.StartsWith("cgroup", StringComparison.Ordinal)) return false;
        foreach (var p in LinuxPseudoMountPrefixes)
            if (root == p || root.StartsWith(p + "/", StringComparison.Ordinal)) return false;
        if (root.StartsWith("/run", StringComparison.Ordinal) && !root.StartsWith("/run/media", StringComparison.Ordinal))
            return false;
        return true;
    }

    private static string SafeFormat(DriveInfo d) { try { return d.DriveFormat; } catch { return ""; } }

    private DriveDto ToDrive(DriveInfo d)
    {
        var fs = SafeFormat(d);
        var dto = new DriveDto
        {
            RootPath = d.RootDirectory.FullName,
            FileSystem = fs,
            Kind = KindOf(d),
            Label = string.IsNullOrWhiteSpace(SafeLabel(d)) ? d.RootDirectory.FullName : SafeLabel(d),
        };
        try { dto.FreeBytes = d.AvailableFreeSpace; dto.TotalBytes = d.TotalSize; } catch { /* ignora */ }
        if (IsFat32(fs)) dto.MaxFileBytes = Fat32MaxFile;
        return dto;
    }

    private static string SafeLabel(DriveInfo d) { try { return d.VolumeLabel ?? ""; } catch { return ""; } }

    private static string KindOf(DriveInfo d)
    {
        try
        {
            return d.DriveType switch
            {
                DriveType.Removable => "removable",
                DriveType.Network => "network",
                _ => "internal",
            };
        }
        catch { return "internal"; }
    }

    private static bool TryWriteProbe(string dir)
    {
        var probe = Path.Combine(dir, $".factum-write-test-{Guid.NewGuid():N}");
        try { File.WriteAllText(probe, "x"); File.Delete(probe); return true; }
        catch { return false; }
    }
}

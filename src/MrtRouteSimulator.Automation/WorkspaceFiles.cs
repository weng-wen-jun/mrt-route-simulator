using System.Text;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.Automation;

/// <summary>File operations are confined to the explicitly configured workspace.</summary>
public sealed class WorkspaceFiles
{
    public string Root { get; }

    public WorkspaceFiles(string root)
    {
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException(Root);
    }

    public string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path, Root);
        var relative = Path.GetRelativePath(Root, full);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("路徑必須位於 MCP 工作目錄內。");
        // Reject junctions/symlinks rather than allowing a lexical path to escape the workspace.
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("MCP 檔案操作不接受符號連結或 junction。");
            if (string.Equals(current, Root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }

    public TopologyProjectDocument ReadProject(string path)
    {
        var full = Resolve(path);
        if (new FileInfo(full).Length > 8_000_000)
            throw new InvalidOperationException("專案超過讀取上限。");
        return TopologyProjectFormat.Deserialize(File.ReadAllText(full));
    }

    public string PrepareOutput(string path, string extension, bool overwrite)
    {
        var full = Resolve(path);
        if (!full.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("輸出副檔名必須是 " + extension);
        if (File.Exists(full) && !overwrite) throw new IOException("檔案已存在；覆寫需明確指定 overwrite=true。");
        if (!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException("請先建立輸出目錄。");
        return full;
    }

    public void WriteAtomically(string path, string text, bool overwrite)
    {
        path = Resolve(path);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
